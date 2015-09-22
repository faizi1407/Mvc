// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNet.Mvc.Abstractions;
using Microsoft.Framework.Internal;

namespace Microsoft.AspNet.Mvc.ModelBinding
{
    /// <summary>
    /// Represents the state of an attempt to bind values from an HTTP Request to an action method, which includes
    /// validation information.
    /// </summary>
    public class ModelStateDictionary : IDictionary<string, ModelState>
    {
        // Make sure to update the doc headers if this value is changed.
        /// <summary>
        /// The default value for <see cref="MaxAllowedErrors"/> of <c>200</c>.
        /// </summary>
        public static readonly int DefaultMaxAllowedErrors = 200;

        private readonly IDictionary<string, ModelState> _innerDictionary;
        private int _maxAllowedErrors;

        /// <summary>
        /// Initializes a new instance of the <see cref="ModelStateDictionary"/> class.
        /// </summary>
        public ModelStateDictionary()
            : this(DefaultMaxAllowedErrors)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ModelStateDictionary"/> class.
        /// </summary>
        public ModelStateDictionary(int maxAllowedErrors)
        {
            MaxAllowedErrors = maxAllowedErrors;

            _innerDictionary = new Dictionary<string, ModelState>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ModelStateDictionary"/> class by using values that are copied
        /// from the specified <paramref name="dictionary"/>.
        /// </summary>
        /// <param name="dictionary">The <see cref="ModelStateDictionary"/> to copy values from.</param>
        public ModelStateDictionary([NotNull] ModelStateDictionary dictionary)
        {
            _innerDictionary = new CopyOnWriteDictionary<string, ModelState>(dictionary,
                                                                             StringComparer.OrdinalIgnoreCase);

            MaxAllowedErrors = dictionary.MaxAllowedErrors;
            ErrorCount = dictionary.ErrorCount;
            HasRecordedMaxModelError = dictionary.HasRecordedMaxModelError;
        }

        /// <summary>
        /// Gets or sets the maximum allowed model state errors in this instance of <see cref="ModelStateDictionary"/>.
        /// Defaults to <c>200</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="ModelStateDictionary"/> tracks the number of model errors added by calls to
        /// <see cref="AddModelError(string, Exception)"/> or <see cref="TryAddModelError(string, Exception)"/>.
        /// Once the value of <code>MaxAllowedErrors - 1</code> is reached, if another attempt is made to add an error,
        /// the error message will be ignored and a <see cref="TooManyModelErrorsException"/> will be added.
        /// </para>
        /// <para>
        /// Errors added via modifying <see cref="ModelState"/> directly do not count towards this limit.
        /// </para>
        /// </remarks>
        public int MaxAllowedErrors
        {
            get
            {
                return _maxAllowedErrors;
            }
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                _maxAllowedErrors = value;
            }
        }

        /// <summary>
        /// Gets a value indicating whether or not the maximum number of errors have been
        /// recorded.
        /// </summary>
        /// <remarks>
        /// Returns <c>true</c> if a <see cref="TooManyModelErrorsException"/> has been recorded;
        /// otherwise <c>false</c>.
        /// </remarks>
        public bool HasReachedMaxErrors
        {
            get { return ErrorCount >= MaxAllowedErrors; }
        }

        /// <summary>
        /// Gets the number of errors added to this instance of <see cref="ModelStateDictionary"/> via
        /// <see cref="AddModelError"/> or <see cref="TryAddModelError"/>.
        /// </summary>
        public int ErrorCount { get; private set; }

        /// <inheritdoc />
        public int Count
        {
            get { return _innerDictionary.Count; }
        }

        /// <inheritdoc />
        public bool IsReadOnly
        {
            get { return _innerDictionary.IsReadOnly; }
        }

        /// <inheritdoc />
        public ICollection<string> Keys
        {
            get { return _innerDictionary.Keys; }
        }

        /// <inheritdoc />
        public ICollection<ModelState> Values
        {
            get { return _innerDictionary.Values; }
        }

        /// <summary>
        /// Gets a value that indicates whether any model state values in this model state dictionary is invalid or not validated.
        /// </summary>
        public bool IsValid
        {
            get
            {
                return ValidationState == ModelValidationState.Valid || ValidationState == ModelValidationState.Skipped;
            }
        }

        /// <inheritdoc />
        public ModelValidationState ValidationState
        {
            get { return GetValidity(_innerDictionary); }
        }

        /// <inheritdoc />
        public ModelState this[[NotNull] string key]
        {
            get
            {
                ModelState value;
                _innerDictionary.TryGetValue(key, out value);
                return value;
            }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }
                _innerDictionary[key] = value;
            }
        }

        // For unit testing
        internal IDictionary<string, ModelState> InnerDictionary
        {
            get { return _innerDictionary; }
        }

        // Flag that indiciates if TooManyModelErrorException has already been added to this dictionary.
        private bool HasRecordedMaxModelError { get; set; }

        /// <summary>
        /// Adds the specified <paramref name="exception"/> to the <see cref="ModelState.Errors"/> instance
        /// that is associated with the specified <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The key of the <see cref="ModelState"/> to add errors to.</param>
        /// <param name="exception">The <see cref="Exception"/> to add.</param>
        public void AddModelError([NotNull] string key, [NotNull] Exception exception)
        {
            TryAddModelError(key, exception);
        }

        /// <summary>
        /// Attempts to add the specified <paramref name="exception"/> to the <see cref="ModelState.Errors"/>
        /// instance that is associated with the specified <paramref name="key"/>. If the maximum number of allowed
        /// errors has already been recorded, records a <see cref="TooManyModelErrorsException"/> exception instead.
        /// </summary>
        /// <param name="key">The key of the <see cref="ModelState"/> to add errors to.</param>
        /// <param name="exception">The <see cref="Exception"/> to add.</param>
        /// <returns>
        /// <c>True</c> if the given error was added, <c>false</c> if the error was ignored.
        /// See <see cref="MaxAllowedErrors"/>.
        /// </returns>
        public bool TryAddModelError([NotNull] string key, [NotNull] Exception exception)
        {
            if (ErrorCount >= MaxAllowedErrors - 1)
            {
                EnsureMaxErrorsReachedRecorded();
                return false;
            }

            if (exception is FormatException)
            {
                // Convert FormatExceptions to Invalid value messages.
                ModelState modelState;
                TryGetValue(key, out modelState);

                string errorMessage;
                if (modelState == null)
                {
                    errorMessage = Resources.FormatModelError_InvalidValue_GenericMessage(key);
                }
                else
                {
                    errorMessage = Resources.FormatModelError_InvalidValue_MessageWithModelValue(
                        modelState.AttemptedValue,
                        key);
                }

                return TryAddModelError(key, errorMessage);
            }

            ErrorCount++;
            AddModelErrorCore(key, exception);
            return true;
        }

        /// <summary>
        /// Adds the specified <paramref name="errorMessage"/> to the <see cref="ModelState.Errors"/> instance
        /// that is associated with the specified <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The key of the <see cref="ModelState"/> to add errors to.</param>
        /// <param name="errorMessage">The error message to add.</param>
        public void AddModelError([NotNull] string key, [NotNull] string errorMessage)
        {
            TryAddModelError(key, errorMessage);
        }

        /// <summary>
        /// Attempts to add the specified <paramref name="errorMessage"/> to the <see cref="ModelState.Errors"/>
        /// instance that is associated with the specified <paramref name="key"/>. If the maximum number of allowed
        /// errors has already been recorded, records a <see cref="TooManyModelErrorsException"/> exception instead.
        /// </summary>
        /// <param name="key">The key of the <see cref="ModelState"/> to add errors to.</param>
        /// <param name="errorMessage">The error message to add.</param>
        /// <returns>
        /// <c>True</c> if the given error was added, <c>false</c> if the error was ignored.
        /// See <see cref="MaxAllowedErrors"/>.
        /// </returns>
        public bool TryAddModelError([NotNull] string key, [NotNull] string errorMessage)
        {
            if (ErrorCount >= MaxAllowedErrors - 1)
            {
                EnsureMaxErrorsReachedRecorded();
                return false;
            }

            ErrorCount++;
            var modelState = GetModelStateForKey(key);
            modelState.ValidationState = ModelValidationState.Invalid;
            modelState.Errors.Add(errorMessage);

            return true;
        }

        /// <summary>
        /// Returns the aggregate <see cref="ModelValidationState"/> for items starting with the
        /// specified <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The key to look up model state errors for.</param>
        /// <returns>Returns <see cref="ModelValidationState.Unvalidated"/> if no entries are found for the specified
        /// key, <see cref="ModelValidationState.Invalid"/> if at least one instance is found with one or more model
        /// state errors; <see cref="ModelValidationState.Valid"/> otherwise.</returns>
        public ModelValidationState GetFieldValidationState([NotNull] string key)
        {
            var entries = FindKeysWithPrefix(key);
            if (!entries.Any())
            {
                return ModelValidationState.Unvalidated;
            }

            return GetValidity(entries);
        }

        /// <summary>
        /// Returns <see cref="ModelValidationState"/> for the <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The key to look up model state errors for.</param>
        /// <returns>Returns <see cref="ModelValidationState.Unvalidated"/> if no entry is found for the specified
        /// key, <see cref="ModelValidationState.Invalid"/> if an instance is found with one or more model
        /// state errors; <see cref="ModelValidationState.Valid"/> otherwise.</returns>
        public ModelValidationState GetValidationState([NotNull] string key)
        {
            ModelState validationState;
            if (TryGetValue(key, out validationState))
            {
                return validationState.ValidationState;
            }

            return ModelValidationState.Unvalidated;
        }

        /// <summary>
        /// Marks the <see cref="ModelState.ValidationState"/> for the entry with the specified <paramref name="key"/>
        /// as <see cref="ModelValidationState.Valid"/>.
        /// </summary>
        /// <param name="key">The key of the <see cref="ModelState"/> to mark as valid.</param>
        public void MarkFieldValid([NotNull] string key)
        {
            var modelState = GetModelStateForKey(key);
            if (modelState.ValidationState == ModelValidationState.Invalid)
            {
                throw new InvalidOperationException(Resources.Validation_InvalidFieldCannotBeReset);
            }

            modelState.ValidationState = ModelValidationState.Valid;
        }

        /// <summary>
        /// Marks the <see cref="ModelState.ValidationState"/> for the entry with the specified <paramref name="key"/>
        /// as <see cref="ModelValidationState.Skipped"/>.
        /// </summary>
        /// <param name="key">The key of the <see cref="ModelState"/> to mark as skipped.</param>
        public void MarkFieldSkipped([NotNull] string key)
        {
            var modelState = GetModelStateForKey(key);
            if (modelState.ValidationState == ModelValidationState.Invalid)
            {
                throw new InvalidOperationException(Resources.Validation_InvalidFieldCannotBeReset_ToSkipped);
            }

            modelState.ValidationState = ModelValidationState.Skipped;
        }

        /// <summary>
        /// Copies the values from the specified <paramref name="dictionary"/> into this instance, overwriting
        /// existing values if keys are the same.
        /// </summary>
        /// <param name="dictionary">The <see cref="ModelStateDictionary"/> to copy values from.</param>
        public void Merge(ModelStateDictionary dictionary)
        {
            if (dictionary == null)
            {
                return;
            }

            foreach (var entry in dictionary)
            {
                this[entry.Key] = entry.Value;
            }
        }

        /// <summary>
        /// Sets the of <see cref="ModelState.RawValue"/> and <see cref="ModelState.AttemptedValue"/> for 
        /// the <see cref="ModelState"/> with the specified <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The key for the <see cref="ModelState"/> entry.</param>
        /// <param name="rawvalue">The raw value for the <see cref="ModelState"/> entry.</param>
        /// <param name="attemptedValue">
        /// The values of <param name="rawValue"/> in a comma-separated <see cref="string"/>.
        /// </param>
        public void SetModelValue([NotNull] string key, object rawValue, string attemptedValue)
        {
            var modelState = GetModelStateForKey(key);
            modelState.RawValue = rawValue;
            modelState.AttemptedValue = attemptedValue;
        }

        /// <summary>
        /// Sets the value for the <see cref="ModelState"/> with the specified <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The key for the <see cref="ModelState"/> entry</param>
        /// <param name="valueProviderResult">
        /// A <see cref="ValueProviderResult"/> with data for the <see cref="ModelState"/> entry.
        /// </param>
        public void SetModelValue([NotNull] string key, ValueProviderResult valueProviderResult)
        {
            // Avoid creating a new array for rawValue if there's only one value.
            object rawValue;
            if (valueProviderResult == ValueProviderResult.None)
            {
                rawValue = null;
            }
            else if (valueProviderResult.Length == 1)
            {
                rawValue = valueProviderResult.Values[0];
            }
            else
            {
                rawValue = valueProviderResult.Values.ToArray();
            }

            SetModelValue(key, rawValue, valueProviderResult.ToString());
        }

        /// <summary>
        /// Clears <see cref="ModelStateDictionary"/> entries that match the key that is passed as parameter.
        /// </summary>
        /// <param name="key">The key of <see cref="ModelStateDictionary"/> to clear.</param>
        public void ClearValidationState(string key)
        {
            // If key is null or empty, clear all entries in the dictionary
            // else just clear the ones that have key as prefix
            var entries  = (string.IsNullOrEmpty(key)) ?
                _innerDictionary : FindKeysWithPrefix(key);

            foreach (var entry in entries)
            {
                entry.Value.Errors.Clear();
                entry.Value.ValidationState = ModelValidationState.Unvalidated;
            }
        }

        private ModelState GetModelStateForKey([NotNull] string key)
        {
            ModelState modelState;
            if (!TryGetValue(key, out modelState))
            {
                modelState = new ModelState();
                this[key] = modelState;
            }

            return modelState;
        }

        private static ModelValidationState GetValidity(IEnumerable<KeyValuePair<string, ModelState>> entries)
        {
            var validationState = ModelValidationState.Valid;
            foreach (var entry in entries)
            {
                var entryState = entry.Value.ValidationState;
                if (entryState == ModelValidationState.Unvalidated)
                {
                    // If any entries of a field is unvalidated, we'll treat the tree as unvalidated.
                    return entryState;
                }
                else if (entryState == ModelValidationState.Invalid)
                {
                    validationState = entryState;
                }
            }
            return validationState;
        }

        private void EnsureMaxErrorsReachedRecorded()
        {
            if (!HasRecordedMaxModelError)
            {
                var exception = new TooManyModelErrorsException(Resources.ModelStateDictionary_MaxModelStateErrors);
                AddModelErrorCore(string.Empty, exception);
                HasRecordedMaxModelError = true;
                ErrorCount++;
            }
        }

        private void AddModelErrorCore(string key, Exception exception)
        {
            var modelState = GetModelStateForKey(key);
            modelState.ValidationState = ModelValidationState.Invalid;
            modelState.Errors.Add(exception);
        }

        /// <inheritdoc />
        public void Add(KeyValuePair<string, ModelState> item)
        {
            Add(item.Key, item.Value);
        }

        /// <inheritdoc />
        public void Add([NotNull] string key, [NotNull] ModelState value)
        {
            _innerDictionary.Add(key, value);
        }

        /// <inheritdoc />
        public void Clear()
        {
            _innerDictionary.Clear();
        }

        /// <inheritdoc />
        public bool Contains(KeyValuePair<string, ModelState> item)
        {
            return _innerDictionary.Contains(item);
        }

        /// <inheritdoc />
        public bool ContainsKey([NotNull] string key)
        {
            return _innerDictionary.ContainsKey(key);
        }

        /// <inheritdoc />
        public void CopyTo([NotNull] KeyValuePair<string, ModelState>[] array, int arrayIndex)
        {
            _innerDictionary.CopyTo(array, arrayIndex);
        }

        /// <inheritdoc />
        public bool Remove(KeyValuePair<string, ModelState> item)
        {
            return _innerDictionary.Remove(item);
        }

        /// <inheritdoc />
        public bool Remove([NotNull] string key)
        {
            return _innerDictionary.Remove(key);
        }

        /// <inheritdoc />
        public bool TryGetValue([NotNull] string key, out ModelState value)
        {
            return _innerDictionary.TryGetValue(key, out value);
        }

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<string, ModelState>> GetEnumerator()
        {
            return _innerDictionary.GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IEnumerable<KeyValuePair<string, ModelState>> FindKeysWithPrefix([NotNull] string prefix)
        {
            return new PrefixEnumerable(this, prefix);
        }

        public static bool IsPrefixMatch(string prefix, string candidate)
        {
            if (candidate.Length <= prefix.Length)
            {
                return false;
            }

            if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {

                if (candidate.StartsWith("[", StringComparison.OrdinalIgnoreCase))
                {
                    var subKey = candidate.Substring(candidate.IndexOf('.') + 1);

                    if (!subKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    if (string.Equals(prefix, subKey, StringComparison.Ordinal))
                    {
                        return true;
                    }

                    candidate = subKey;
                }
                else
                {
                    return false;
                }
            }

            // Everything is prefixed by the empty string
            if (prefix.Length == 0)
            {
                return true;
            }
            else
            {
                var charAfterPrefix = candidate[prefix.Length];
                switch (charAfterPrefix)
                {
                    case '[':
                    case '.':
                        return true;
                }
            }

            return false;
        }

        public struct PrefixEnumerable : IEnumerable<KeyValuePair<string, ModelState>>
        {
            private readonly ModelStateDictionary _modelState;
            private readonly string _prefix;

            public PrefixEnumerable(ModelStateDictionary modelState, string prefix)
            {
                _modelState = modelState;
                _prefix = prefix;
            }

            public PrefixEnumerator GetEnumerator()
            {
                return new PrefixEnumerator(_modelState, _prefix);
            }

            IEnumerator<KeyValuePair<string, ModelState>> IEnumerable<KeyValuePair<string, ModelState>>.GetEnumerator()
            {
                return GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        public struct PrefixEnumerator : IEnumerator<KeyValuePair<string, ModelState>>
        {
            private readonly ModelStateDictionary _modelState;
            private readonly string _prefix;

            private IEnumerator<KeyValuePair<string, ModelState>> _enumerator;
            private ModelState _first;

            public PrefixEnumerator(ModelStateDictionary modelState, string prefix)
            {
                _modelState = modelState;
                _prefix = prefix;

                _enumerator = null;
                _first = null;
            }

            public KeyValuePair<string, ModelState> Current
            {
                get
                {
                    if (_first != null)
                    {
                        return new KeyValuePair<string, ModelState>(_prefix, _first);
                    }
                    else if (_enumerator != null)
                    {
                        return _enumerator.Current;
                    }
                    else
                    {
                        return default(KeyValuePair<string, ModelState>);
                    }
                }
            }

            object IEnumerator.Current
            {
                get
                {
                    return Current;
                }
            }

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                if (_enumerator == null)
                {
                    _enumerator = _modelState.GetEnumerator();
                    if (_modelState.TryGetValue(_prefix, out _first))
                    {
                        return true;
                    }
                }

                _first = null;

                while (_enumerator.MoveNext())
                {
                    if (IsPrefixMatch(_prefix, _enumerator.Current.Key))
                    {
                        return true;
                    }
                }

                return false;
            }

            public void Reset()
            {
                _enumerator = null;
            }
        }
    }
}