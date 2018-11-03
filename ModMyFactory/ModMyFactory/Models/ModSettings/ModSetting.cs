﻿using ModMyFactory.ModSettings;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using WPFCore;

namespace ModMyFactory.Models.ModSettings
{
    abstract class ModSetting<T> : NotifyPropertyChangedBase, IModSetting<T> where T : IEquatable<T>
    {
        T value;

        public IHasModSettings Owner { get; }

        public string Name { get; }

        public LoadTime LoadTime { get; }

        public string Ordering { get; }

        public virtual T Value
        {
            get => value;
            set
            {
                var comparer = EqualityComparer<T>.Default;
                if (!comparer.Equals(value, this.value))
                {
                    this.value = value;
                    OnPropertyChanged(new PropertyChangedEventArgs(nameof(Value)));
                }
            }
        }

        public T DefaultValue { get; }

        public abstract DataTemplate Template { get; }

        protected ModSetting(IHasModSettings owner, string name, LoadTime loadTime, string ordering, T defaultValue)
        {
            Owner = owner;
            Name = name;
            LoadTime = loadTime;
            Ordering = ordering;

            Value = defaultValue;
            DefaultValue = defaultValue;
        }
        
        public virtual void ResetToDefault()
        {
            Value = DefaultValue;
        }

        public abstract IModSettingProxy CreateProxy();
    }
}
