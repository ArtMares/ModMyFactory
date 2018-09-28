﻿using ModMyFactory.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ModMyFactory.Helpers;

namespace ModMyFactory.Web
{
    /// <summary>
    /// Represents the factorio.com website.
    /// </summary>
    static class FactorioWebsite
    {
        /// <summary>
        /// Logs in at the website.
        /// </summary>
        /// <param name="container">The cookie container to store the session cookie in.</param>
        /// <param name="username">The username.</param>
        /// <param name="password">The users password.</param>
        /// <returns>Returns false if the login failed, otherwise true.</returns>
        public static bool LogIn(CookieContainer container, string username, SecureString password)
        {
            const string loginPage = "https://www.factorio.com/login";
            const string pattern = @"[0-9a-zA-Z_\-]{56}\.[0-9a-zA-Z_\-]{6}\.[0-9a-zA-Z_\-]{27}";

            // Get a csrf token.
            string document = WebHelper.GetDocument(loginPage, container);
            MatchCollection matches = Regex.Matches(document, pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            if (matches.Count != 1) return false;
            string csrfToken = matches[0].Value;

            // Log in using the token and credentials.
            string part1 = $"csrf_token={csrfToken}&username_or_email={username}&password=";
            int part1Length = Encoding.UTF8.GetByteCount(part1);
            int part2Length = SecureStringHelper.GetSecureStringByteCount(password);
            string part3 = "&action=Login";
            int part3Length = Encoding.UTF8.GetByteCount(part3);

            byte[] content = new byte[part1Length + part2Length + part3Length];
            Encoding.UTF8.GetBytes(part1, 0, part1.Length, content, 0);
            SecureStringHelper.SecureStringToBytes(password, content, part1Length);
            Encoding.UTF8.GetBytes(part3, 0, part3.Length, content, part1Length + part2Length);

            try
            {
                document = WebHelper.GetDocument(loginPage, container, content);
                if (!document.Contains("logout")) return false;

                return true;
            }
            finally
            {
                SecureStringHelper.DestroySecureByteArray(content);
            }
        }

        /// <summary>
        /// Ensures a session is logged in at the website.
        /// </summary>
        /// <param name="container">The cookie container the session cookie is stored in.</param>
        /// <returns>Returns true if the session is logged in, otherwise false.</returns>
        public static bool EnsureLoggedIn(CookieContainer container)
        {
            const string mainPage = "https://www.factorio.com";

            string document = WebHelper.GetDocument(mainPage, container);
            return document.Contains("logout");
        }

        private static bool VersionCompatibleWithPlatform(Version version)
        {
            if (Environment.Is64BitOperatingSystem)
            {
                return true;
            }
            else
            {
                // 32 bit no longer supported as of version 0.15.
                return version < new Version(0, 15);
            }
        }

        /// <summary>
        /// Reads the Factorio version list.
        /// </summary>
        /// <param name="container">The cookie container the session cookie is stored in.</param>
        /// <param name="versions">Out. The list of available Factorio versions.</param>
        /// <returns>Returns false if the version list could not be retrieved, otherwise true.</returns>
        public static bool GetVersions(CookieContainer container, out List<FactorioOnlineVersion> versions)
        {
            const string downloadPage = "https://www.factorio.com/download";
            const string experimentalDownloadPage = "https://www.factorio.com/download/experimental";
            const string pattern = @"<h3> *(?<version>[0-9]+\.[0-9]+\.[0-9]+) *\((?<modifier>[a-z]+)\) *</h3>";
            string[] allowedModifiers = { "alpha" };

            versions = new List<FactorioOnlineVersion>();

            // Get stable versions.
            string document = WebHelper.GetDocument(downloadPage, container);
            MatchCollection matches = Regex.Matches(document, pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                string versionString = match.Groups["version"].Value;
                string modifierString = match.Groups["modifier"].Value;
                Version version = Version.Parse(versionString);

                if (allowedModifiers.Contains(modifierString) && VersionCompatibleWithPlatform(version))
                {
                    var factorioVersion = new FactorioOnlineVersion(version, modifierString, false);
                    versions.Add(factorioVersion);
                }
            }

            // Get experimental versions.
            document = WebHelper.GetDocument(experimentalDownloadPage, container);
            matches = Regex.Matches(document, pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                string versionString = match.Groups["version"].Value;
                string modifierString = match.Groups["modifier"].Value;

                if (allowedModifiers.Contains(modifierString))
                {
                    var version = new FactorioOnlineVersion(Version.Parse(versionString), modifierString, true);
                    versions.Add(version);
                }
            }

            return true;
        }

        /// <summary>
        /// Downloads Factorio.
        /// </summary>
        /// <param name="version">The version of Factorio to be downloaded.</param>
        /// <param name="downloadDirectory">The directory the file is downloaded to.</param>
        /// <param name="container">The cookie container the session cookie is stored in.</param>
        /// <param name="progress">A progress object used to report the progress of the operation.</param>
        /// <param name="cancellationToken">A cancelation token that can be used to cancel the operation.</param>
        public static async Task<FactorioVersion> DownloadFactorioAsync(FactorioOnlineVersion version, DirectoryInfo downloadDirectory, CookieContainer container, IProgress<double> progress, CancellationToken cancellationToken)
        {
            //if (!downloadDirectory.Exists) downloadDirectory.Create();

            //string filePath = Path.Combine(downloadDirectory.FullName, "package.zip");
            //var file = new FileInfo(filePath);

            //await WebHelper.DownloadFileAsync(version.DownloadUrl, container, file, progress, cancellationToken);
            //if (!cancellationToken.IsCancellationRequested)
            //{
            //    progress.Report(2);
            //    DirectoryInfo dir = await Task.Run(() =>
            //    {
            //        ZipFile.ExtractToDirectory(file.FullName, downloadDirectory.FullName);
            //        file.Delete();

            //        return downloadDirectory.EnumerateDirectories($"Factorio_{version.Version}*").First();
            //    });


            //}

            return null;
        }
    }
}
