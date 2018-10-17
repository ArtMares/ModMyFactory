﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using WPFCore;

namespace ModMyFactory.Models.ModSettings
{
    abstract class ModSetting<T> : NotifyPropertyChangedBase, IModSetting where T : IEquatable<T>
    {
        T value;

        public string Name { get; }

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

        protected ModSetting(string name, string ordering, T defaultValue)
        {
            Name = name;
            Ordering = ordering;

            Value = defaultValue;
            DefaultValue = defaultValue;
        }
        
        public void ResetToDefault()
        {
            Value = DefaultValue;
        }
    }
}
