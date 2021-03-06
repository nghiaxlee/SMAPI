using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI.Framework.ModLoading;
using StardewModdingAPI.Framework.Reflection;
using StardewValley;

namespace StardewModdingAPI.Framework.Content
{
    /// <summary>A low-level wrapper around the content cache which handles reading, writing, and invalidating entries in the cache. This doesn't handle any higher-level logic like localisation, loading content, etc. It assumes all keys passed in are already normalised.</summary>
    internal class ContentCache
    {
        /*********
        ** Properties
        *********/
        /// <summary>The underlying asset cache.</summary>
        private readonly IDictionary<string, object> Cache;

        /// <summary>The possible directory separator characters in an asset key.</summary>
        private readonly char[] PossiblePathSeparators;

        /// <summary>The preferred directory separator chaeacter in an asset key.</summary>
        private readonly string PreferredPathSeparator;

        /// <summary>Applies platform-specific asset key normalisation so it's consistent with the underlying cache.</summary>
        private readonly Func<string, string> NormaliseAssetNameForPlatform;


        /*********
        ** Accessors
        *********/
        /// <summary>Get or set the value of a raw cache entry.</summary>
        /// <param name="key">The cache key.</param>
        public object this[string key]
        {
            get => this.Cache[key];
            set => this.Cache[key] = value;
        }

        /// <summary>The current cache keys.</summary>
        public IEnumerable<string> Keys => this.Cache.Keys;


        /*********
        ** Public methods
        *********/
        /****
        ** Constructor
        ****/
        /// <summary>Construct an instance.</summary>
        /// <param name="contentManager">The underlying content manager whose cache to manage.</param>
        /// <param name="reflection">Simplifies access to private game code.</param>
        /// <param name="possiblePathSeparators">The possible directory separator characters in an asset key.</param>
        /// <param name="preferredPathSeparator">The preferred directory separator chaeacter in an asset key.</param>
        public ContentCache(LocalizedContentManager contentManager, Reflector reflection, char[] possiblePathSeparators, string preferredPathSeparator)
        {
            // init
            this.Cache = reflection.GetPrivateField<Dictionary<string, object>>(contentManager, "loadedAssets").GetValue();
            this.PossiblePathSeparators = possiblePathSeparators;
            this.PreferredPathSeparator = preferredPathSeparator;

            // get key normalisation logic
            if (Constants.TargetPlatform == Platform.Windows)
            {
                IPrivateMethod method = reflection.GetPrivateMethod(typeof(TitleContainer), "GetCleanPath");
                this.NormaliseAssetNameForPlatform = path => method.Invoke<string>(path);
            }
            else
                this.NormaliseAssetNameForPlatform = key => key.Replace('\\', '/'); // based on MonoGame's ContentManager.Load<T> logic
        }

        /****
        ** Fetch
        ****/
        /// <summary>Get whether the cache contains a given key.</summary>
        /// <param name="key">The cache key.</param>
        public bool ContainsKey(string key)
        {
            return this.Cache.ContainsKey(key);
        }


        /****
        ** Normalise
        ****/
        /// <summary>Normalise path separators in a file path. For asset keys, see <see cref="NormaliseKey"/> instead.</summary>
        /// <param name="path">The file path to normalise.</param>
        [Pure]
        public string NormalisePathSeparators(string path)
        {
            string[] parts = path.Split(this.PossiblePathSeparators, StringSplitOptions.RemoveEmptyEntries);
            string normalised = string.Join(this.PreferredPathSeparator, parts);
            if (path.StartsWith(this.PreferredPathSeparator))
                normalised = this.PreferredPathSeparator + normalised; // keep root slash
            return normalised;
        }

        /// <summary>Normalise a cache key so it's consistent with the underlying cache.</summary>
        /// <param name="key">The asset key.</param>
        [Pure]
        public string NormaliseKey(string key)
        {
            key = this.NormalisePathSeparators(key);
            return key.EndsWith(".xnb", StringComparison.InvariantCultureIgnoreCase)
                ? key.Substring(0, key.Length - 4)
                : this.NormaliseAssetNameForPlatform(key);
        }

        /****
        ** Remove
        ****/
        /// <summary>Remove an asset with the given key.</summary>
        /// <param name="key">The cache key.</param>
        /// <param name="dispose">Whether to dispose the entry value, if applicable.</param>
        /// <returns>Returns the removed key (if any).</returns>
        public bool Remove(string key, bool dispose)
        {
            // get entry
            if (!this.Cache.TryGetValue(key, out object value))
                return false;

            // dispose & remove entry
            if (dispose && value is IDisposable disposable)
                disposable.Dispose();

            return this.Cache.Remove(key);
        }

        /// <summary>Purge matched assets from the cache.</summary>
        /// <param name="predicate">Matches the asset keys to invalidate.</param>
        /// <param name="dispose">Whether to dispose invalidated assets. This should only be <c>true</c> when they're being invalidated as part of a dispose, to avoid crashing the game.</param>
        /// <returns>Returns the removed keys (if any).</returns>
        public IEnumerable<string> Remove(Func<string, Type, bool> predicate, bool dispose = false)
        {
            List<string> removed = new List<string>();
            foreach (string key in this.Cache.Keys.ToArray())
            {
                Type type = this.Cache[key].GetType();
                if (predicate(key, type))
                {
                    this.Remove(key, dispose);
                    removed.Add(key);
                }
            }
            return removed;
        }
    }
}
