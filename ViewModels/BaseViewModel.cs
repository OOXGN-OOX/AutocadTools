// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

// File: ViewModels/BaseViewModel.cs

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace AutocadTools.ViewModels
{
    /// <summary>
    /// ������� ����� ��� ���� ViewModel � ���������� INotifyPropertyChanged
    /// ������������� �������� ����������� UI �� ��������� �������
    /// </summary>
    public abstract class BaseViewModel : INotifyPropertyChanged, IDisposable
    {
        #region INotifyPropertyChanged

        /// <summary>
        /// ������� ��������� �������� ��� ��������������� ���������� UI
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// ����� ������� PropertyChanged ��� ����������� UI �� ��������� ��������
        /// </summary>
        /// <param name="propertyName">��� �������� (������������� ����� CallerMemberName)</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// ������������� ����� ��������� �������� �������� � �������������� ������������
        /// </summary>
        /// <typeparam name="T">��� ��������</typeparam>
        /// <param name="field">������ �� backing field</param>
        /// <param name="value">����� ��������</param>
        /// <param name="propertyName">��� �������� (�������������)</param>
        /// <returns>true ���� �������� ����������, false ���� �������� �������</returns>
        protected virtual bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// ��������� �������� � �������������� ��������� ����� ���������
        /// </summary>
        protected virtual bool SetProperty<T>(ref T field, T value, Action onChanged, [CallerMemberName] string propertyName = null)
        {
            if (SetProperty(ref field, value, propertyName))
            {
                onChanged?.Invoke();
                return true;
            }
            return false;
        }

        #endregion

        #region Async Operations Support

        private int _runningOperations = 0;

        /// <summary>
        /// ���� ������� ������������� ����������� ��������
        /// </summary>
        public bool HasRunningOperations => _runningOperations > 0;

        /// <summary>
        /// ���������� ����������� �������� � �������������� ����������� ������ IsBusy
        /// </summary>
        protected async Task ExecuteAsync(Func<Task> operation, Action<Exception> errorHandler = null)
        {
            try
            {
                _runningOperations++;
                OnPropertyChanged(nameof(HasRunningOperations));

                await operation();
            }
            catch (Exception ex)
            {
                if (errorHandler != null)
                    errorHandler(ex);
                else
                    throw;
            }
            finally
            {
                _runningOperations--;
                OnPropertyChanged(nameof(HasRunningOperations));
            }
        }

        /// <summary>
        /// ���������� ����������� �������� � ������������ ���������
        /// </summary>
        protected async Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> operation, Action<Exception> errorHandler = null)
        {
            try
            {
                _runningOperations++;
                OnPropertyChanged(nameof(HasRunningOperations));

                return await operation();
            }
            catch (Exception ex)
            {
                if (errorHandler != null)
                {
                    errorHandler(ex);
                    return default(TResult);
                }
                else
                    throw;
            }
            finally
            {
                _runningOperations--;
                OnPropertyChanged(nameof(HasRunningOperations));
            }
        }

        #endregion

        #region Validation Support

        private readonly Dictionary<string, List<string>> _errors = new Dictionary<string, List<string>>();

        /// <summary>
        /// �������� ������� ������ ���������
        /// </summary>
        public bool HasErrors => _errors.Count > 0;

        /// <summary>
        /// ��������� ������ ��� ����������� ��������
        /// </summary>
        public IEnumerable<string> GetErrors(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
                return null;

            if (_errors.TryGetValue(propertyName, out var errors))
                return errors;

            return null;
        }

        /// <summary>
        /// ���������� ������ ��������� ��� ��������
        /// </summary>
        protected void AddError(string propertyName, string error)
        {
            if (!_errors.ContainsKey(propertyName))
                _errors[propertyName] = new List<string>();

            if (!_errors[propertyName].Contains(error))
            {
                _errors[propertyName].Add(error);
                OnPropertyChanged(nameof(HasErrors));
            }
        }

        /// <summary>
        /// ������� ������ ��� ��������
        /// </summary>
        protected void ClearErrors(string propertyName)
        {
            if (_errors.ContainsKey(propertyName))
            {
                _errors.Remove(propertyName);
                OnPropertyChanged(nameof(HasErrors));
            }
        }

        /// <summary>
        /// ������� ���� ������ ���������
        /// </summary>
        protected void ClearAllErrors()
        {
            _errors.Clear();
            OnPropertyChanged(nameof(HasErrors));
        }

        #endregion

        #region IDisposable Support

        private bool _disposed = false;

        /// <summary>
        /// ������������ �������� ViewModel
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// ������������ ����������� � ������������� ��������
        /// </summary>
        /// <param name="disposing">true ���� ������� �� Dispose(), false ���� �� ������������</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // ������������ ����������� ��������
                    OnDisposing();
                }

                // ������������ ������������� �������� (���� ����)

                _disposed = true;
            }
        }

        /// <summary>
        /// ���������������� ����� ��� ������� �������� � �����������
        /// </summary>
        protected virtual void OnDisposing()
        {
            // �������������� � ����������� ��� ������� ����������� ��������
        }

        /// <summary>
        /// ���������� (�����������)
        /// </summary>
        ~BaseViewModel()
        {
            Dispose(false);
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// �������� �� null � ���������� ����������
        /// </summary>
        protected void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);
        }

        /// <summary>
        /// ����������� �� ��������� ���������� ������� ������������
        /// </summary>
        protected void OnPropertiesChanged(params string[] propertyNames)
        {
            if (propertyNames == null || propertyNames.Length == 0)
                return;

            foreach (var propertyName in propertyNames)
            {
                OnPropertyChanged(propertyName);
            }
        }

        #endregion
    }
}
