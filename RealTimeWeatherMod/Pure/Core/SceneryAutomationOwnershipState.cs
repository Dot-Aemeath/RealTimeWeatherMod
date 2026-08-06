using System.Collections.Generic;

namespace ChillWithYou.EnvSync.Core
{
    internal struct OwnershipTransition
    {
        internal OwnershipTransition(bool userOwnershipAdded, bool managedOwnershipReleased)
        {
            UserOwnershipAdded = userOwnershipAdded;
            ManagedOwnershipReleased = managedOwnershipReleased;
        }

        internal bool UserOwnershipAdded { get; }
        internal bool ManagedOwnershipReleased { get; }
        internal bool Changed => UserOwnershipAdded || ManagedOwnershipReleased;
    }

    /// <summary>
    /// Owns the user-priority invariant for scenery automation. A user-owned item
    /// cannot simultaneously be auto-managed or be reclaimed by automation.
    ///
    /// Threading: main thread only. Every caller — Harmony patches, MonoBehaviour
    /// Update/coroutines, and the MCP debug bridge via MainThreadDispatcher — runs on
    /// Unity's main thread, so this type carries no lock. The snapshot methods exist
    /// to allow mutation while iterating, not for thread safety.
    /// </summary>
    internal sealed class SceneryAutomationOwnershipState<T>
    {
        private readonly HashSet<T> _managed;
        private readonly HashSet<T> _userOwned;

        internal SceneryAutomationOwnershipState()
            : this(null)
        {
        }

        internal SceneryAutomationOwnershipState(IEqualityComparer<T> comparer)
        {
            _managed = new HashSet<T>(comparer);
            _userOwned = new HashSet<T>(comparer);
        }

        internal int ManagedCount => _managed.Count;

        internal int UserOwnedCount => _userOwned.Count;

        internal bool IsManaged(T item)
        {
            return _managed.Contains(item);
        }

        internal bool IsUserOwned(T item)
        {
            return _userOwned.Contains(item);
        }

        /// <summary>
        /// Returns false when the item is already managed, or when the user owns it and
        /// automation may not reclaim it. Callers that care about the difference should
        /// check <see cref="IsUserOwned"/> — silently dropping this result is how a
        /// restore can appear to succeed while doing nothing.
        /// </summary>
        internal bool TryMarkManaged(T item)
        {
            return !_userOwned.Contains(item) && _managed.Add(item);
        }

        internal bool ReleaseManaged(T item)
        {
            return _managed.Remove(item);
        }

        internal OwnershipTransition RecordUserInteraction(T item)
        {
            bool userAdded = _userOwned.Add(item);
            bool managedReleased = _managed.Remove(item);
            return new OwnershipTransition(userAdded, managedReleased);
        }

        internal void ClearManaged()
        {
            _managed.Clear();
        }

        /// <summary>Copy for iteration while the underlying set is being mutated.</summary>
        internal List<T> ManagedSnapshot()
        {
            return new List<T>(_managed);
        }

        internal List<T> UserOwnedSnapshot()
        {
            return new List<T>(_userOwned);
        }
    }
}
