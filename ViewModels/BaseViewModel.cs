// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AutocadTools.ViewModels
{
    public abstract class BaseViewModel : INotifyPropertyChanged, IDisposable
    {
        private bool _disposed;
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        protected virtual bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected virtual bool SetProperty<T>(ref T field, T value, Action onChanged, [CallerMemberName] string propertyName = null)
        {
            if (!SetProperty(ref field, value, propertyName)) return false;
            onChanged?.Invoke();
            return true;
        }

        protected void OnPropertiesChanged(params string[] names)
        {
            foreach (var name in names) OnPropertyChanged(name);
        }

        protected void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);
        }

        protected virtual void OnDisposing() { }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { OnDisposing(); }
            finally { PropertyChanged = null; }
        }
    }
}
