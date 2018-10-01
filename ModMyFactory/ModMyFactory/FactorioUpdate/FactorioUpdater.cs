﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModMyFactory.Helpers;
using ModMyFactory.Models;
using ModMyFactory.Web;
using ModMyFactory.Web.UpdateApi;
using Xdelta;

namespace ModMyFactory.FactorioUpdate
{
    /// <summary>
    /// Updates Factorio.
    /// </summary>
    static class FactorioUpdater
    {
        private static UpdateStep GetOptimalStep(IEnumerable<UpdateStep> updateSteps, Version from, Version maxTo)
        {
            return updateSteps.Where(step => (step.From == from) && (step.To <= maxTo)).MaxBy(step => step.To, new VersionComparer());
        }

        private static List<UpdateStep> GetStepChain(IEnumerable<UpdateStep> updateSteps, Version from, Version to)
        {
            var chain = new List<UpdateStep>();

            UpdateStep currentStep = GetOptimalStep(updateSteps, from, to);
            chain.Add(currentStep);

            while (currentStep.To < to)
            {
                UpdateStep nextStep = GetOptimalStep(updateSteps, currentStep.To, to);
                chain.Add(nextStep);

                currentStep = nextStep;
            }

            return chain;
        }

        /// <summary>
        /// Gets all valid target versions for a specified version of Factorio.
        /// </summary>
        /// <param name="versionToUpdate">The version of Factorio that is to be updated.</param>
        /// <param name="updateSteps">The available update steps provided by the API.</param>
        /// <returns>Returns a list of valid update targets for the specified version of Factorio.</returns>
        public static List<UpdateTarget> GetUpdateTargets(FactorioVersion versionToUpdate, List<UpdateStep> updateSteps)
        {
            var targets = new List<UpdateTarget>();
            var groups = updateSteps.GroupBy(step => new Version(step.To.Major, step.To.Minor));
            foreach (var group in groups)
            {
                UpdateStep targetStep = group.MaxBy(step => step.To, new VersionComparer());
                List<UpdateStep> stepChain = GetStepChain(updateSteps, versionToUpdate.Version, targetStep.To);
                UpdateTarget target = new UpdateTarget(stepChain, targetStep.To, targetStep.IsStable);
                targets.Add(target);

                if (!targetStep.IsStable)
                {
                    UpdateStep stableStep = group.Where(step => step.IsStable).MaxBy(step => step.To, new VersionComparer());
                    if (stableStep != null)
                    {
                        stepChain = GetStepChain(updateSteps, versionToUpdate.Version, stableStep.To);
                        target = new UpdateTarget(stepChain, stableStep.To, true);
                        targets.Add(target);
                    }
                }
            }
            return targets;
        }

        /// <summary>
        /// Downloads all required packages to update to a specified update target.
        /// </summary>
        /// <param name="username">The username.</param>
        /// <param name="token">The login token.</param>
        /// <param name="target">The update target.</param>
        /// <param name="progress">A progress object used to report the progress of the operation.</param>
        /// <param name="cancellationToken">A cancelation token that can be used to cancel the operation.</param>
        /// <returns>Returns a list of update package files.</returns>
        public static async Task<List<FileInfo>> DownloadUpdatePackagesAsync(string username, string token, UpdateTarget target, IProgress<double> progress, CancellationToken cancellationToken)
        {
            var packageFiles = new List<FileInfo>();

            try
            {
                int stepCount = target.Steps.Count;
                int counter = 0;
                foreach (var step in target.Steps)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    var subProgress = new Progress<double>(value => progress.Report((1.0 / stepCount) * counter + (value / stepCount)));
                    var packageFile = await UpdateWebsite.DownloadUpdatePackageAsync(username, token, step, subProgress, cancellationToken);
                    if (packageFile != null) packageFiles.Add(packageFile);

                    counter++;
                }
                progress.Report(1);

                if (cancellationToken.IsCancellationRequested)
                {
                    foreach (var file in packageFiles)
                    {
                        if (file.Exists)
                            file.Delete();
                    }

                    return null;
                }

                return packageFiles;
            }
            catch (Exception)
            {
                foreach (var file in packageFiles)
                {
                    if (file.Exists)
                        file.Delete();
                }

                throw;
            }
        }

        private static async Task<UpdatePackageInfo> GetUpdatePackageInfoAsync(ZipArchive archive)
        {
            return await Task.Run(() =>
            {
                UpdatePackageInfo result = null;

                foreach (var entry in archive.Entries)
                {
                    if (entry.Name.Equals("info.json", StringComparison.InvariantCultureIgnoreCase) && !entry.FullName.Contains("__PATH__read-data__"))
                    {
                        using (var stream = entry.Open())
                        {
                            using (var reader = new StreamReader(stream))
                            {
                                string json = reader.ReadToEnd();
                                result = JsonHelper.Deserialize<UpdatePackageInfo>(json);
                            }
                        }
                        int index = entry.FullName.LastIndexOf('/');
                        result.PackageDirectory = index > 0 ? entry.FullName.Substring(0, index) : string.Empty;

                        break;
                    }
                }

                if (result == null) throw new CriticalUpdaterException(UpdaterErrorType.PackageInvalid);
                return result;
            });
        }

        private static string ResolveArchivePath(string path)
        {
            string[] parts = path.Split('/');
            var dirList = new List<string>(parts.Length);

            foreach (string part in parts)
            {
                if (part == "..")
                    dirList.RemoveAt(dirList.Count - 1);
                else
                    dirList.Add(part);
            }

            return string.Join("/", dirList);
        }

        private static async Task AddFileAsync(FileUpdateInfo fileUpdate, FactorioVersion versionToUpdate, ZipArchive archive, string packageDirectory)
        {
            await Task.Run(() =>
            {
                var file = new FileInfo(versionToUpdate.ExpandPathVariables(fileUpdate.Path));

                string entryPath = fileUpdate.Path;
                if (!string.IsNullOrEmpty(packageDirectory)) entryPath = string.Join("/", packageDirectory, entryPath);
                entryPath = ResolveArchivePath(entryPath);
                var entry = archive.GetEntry(entryPath);

                var dir = file.Directory;
                if (!dir.Exists) dir.Create();
                entry.ExtractToFile(file.FullName, true);
            });
        }

        private static async Task DeleteFileAsync(FileUpdateInfo fileUpdate, FactorioVersion versionToUpdate)
        {
            await Task.Run(() =>
            {
                var file = new FileInfo(versionToUpdate.ExpandPathVariables(fileUpdate.Path));
                if (file.Exists) file.Delete();
            });
        }

        private static async Task UpdateFileAsync(FileUpdateInfo fileUpdate, FactorioVersion versionToUpdate, ZipArchive archive, string packageDirectory)
        {
            await Task.Run(() =>
            {
                var file = new FileInfo(versionToUpdate.ExpandPathVariables(fileUpdate.Path));
                if (!file.Exists) throw new CriticalUpdaterException(UpdaterErrorType.FileNotFound);

                uint oldCrc = file.CalculateCrc();
                if (oldCrc != fileUpdate.OldCrc) throw new CriticalUpdaterException(UpdaterErrorType.ChecksumMismatch);

                string entryPath = fileUpdate.Path;
                if (!string.IsNullOrEmpty(packageDirectory)) entryPath = string.Join("/", packageDirectory, entryPath);
                entryPath = ResolveArchivePath(entryPath);
                var entry = archive.GetEntry(entryPath);

                using (var output = new MemoryStream())
                {
                    using (var input = file.OpenRead())
                    {
                        using (var patch = new MemoryStream())
                        {
                            using (var source = entry.Open())
                                source.CopyTo(patch);
                            patch.Position = 0;

                            var decoder = new Decoder(input, patch, output);
                            decoder.Run();
                        }
                    }

                    output.Position = 0;
                    using (var destination = file.Open(FileMode.Truncate, FileAccess.Write))
                        output.CopyTo(destination);
                }

                uint newCrc = file.CalculateCrc();
                if (newCrc != fileUpdate.NewCrc) throw new CriticalUpdaterException(UpdaterErrorType.ChecksumMismatch);
            });
        }

        private static async Task ApplyUpdatePackageAsync(FactorioVersion versionToUpdate, FileInfo packageFile, IProgress<double> progress)
        {
            using (var archive = ZipFile.OpenRead(packageFile.FullName))
            {
                UpdatePackageInfo packageInfo = await GetUpdatePackageInfoAsync(archive);
                if (!string.Equals(packageInfo.Type, "update", StringComparison.InvariantCultureIgnoreCase))
                    throw new CriticalUpdaterException(UpdaterErrorType.PackageInvalid);

                int fileCount = packageInfo.UpdatedFiles.Length;
                int counter = 0;
                foreach (var fileUpdate in packageInfo.UpdatedFiles)
                {
                    progress.Report((double)counter / fileCount);

                    switch (fileUpdate.Action)
                    {
                        case FileUpdateAction.Added:
                            await AddFileAsync(fileUpdate, versionToUpdate, archive, packageInfo.PackageDirectory);
                            break;
                        case FileUpdateAction.Removed:
                            await DeleteFileAsync(fileUpdate, versionToUpdate);
                            break;
                        case FileUpdateAction.Differs:
                            await UpdateFileAsync(fileUpdate, versionToUpdate, archive, packageInfo.PackageDirectory);
                            break;
                    }

                    counter++;
                }
                progress.Report(1);
            }
        }

        /// <summary>
        /// Downloads and applies an update target to a specified version of Factorio.
        /// </summary>
        /// <param name="versionToUpdate">The version of Factorio that is going to be updated.</param>
        /// <param name="username">The username.</param>
        /// <param name="token">The login token.</param>
        /// <param name="target">The update target.</param>
        /// <param name="progress">A progress object used to report the progress of the operation.</param>
        /// <param name="stageProgress">A progress object used to report the stage of the operation.</param>
        /// <param name="cancellationToken">A cancelation token that can be used to cancel the operation.</param>
        public static async Task ApplyUpdatePackagesAsync(FactorioVersion versionToUpdate, List<FileInfo> packageFiles, IProgress<double> progress)
        {
            int packageCount = packageFiles.Count;
            int counter = 0;

            var p = new Progress<double>(value => progress.Report((counter + value) / packageCount));

            foreach (var packageFile in packageFiles)
            {
                await ApplyUpdatePackageAsync(versionToUpdate, packageFile, p);

                counter++;
            }

            progress.Report(1);
        }
    }
}
