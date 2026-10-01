using System;
using System.Collections.Generic;
using Ion.Projection;
using UnityEngine;

namespace Ion.Gameplay
{
    /// <summary>The photos the player is carrying, plus which one is selected (-1 when empty).</summary>
    [DisallowMultipleComponent]
    public sealed class PhotoInventory : MonoBehaviour
    {
        readonly List<PhotoData> _photos = new List<PhotoData>(8);
        int _selected = -1;

        public IReadOnlyList<PhotoData> Photos => _photos;
        public int Count => _photos.Count;

        /// <summary>Raised whenever the list or the selection changes.</summary>
        public event Action Changed;

        /// <summary>Index of the selected photo; clamped to the list, -1 when empty.</summary>
        public int SelectedIndex
        {
            get => _selected;
            set
            {
                int v = _photos.Count == 0 ? -1 : Mathf.Clamp(value, 0, _photos.Count - 1);
                if (v == _selected) return;
                _selected = v;
                Changed?.Invoke();
            }
        }

        public PhotoData Selected =>
            _selected >= 0 && _selected < _photos.Count ? _photos[_selected] : null;

        /// <summary>Adds a photo at the end and selects it. Duplicates and nulls are ignored.</summary>
        public void Add(PhotoData p)
        {
            if (p == null || _photos.Contains(p)) return;
            _photos.Add(p);
            _selected = _photos.Count - 1;
            Changed?.Invoke();
        }

        /// <summary>Inserts a photo at <paramref name="index"/> (clamped) and selects it.</summary>
        public void Insert(int index, PhotoData p)
        {
            if (p == null || _photos.Contains(p)) return;
            index = Mathf.Clamp(index, 0, _photos.Count);
            _photos.Insert(index, p);
            _selected = index;
            Changed?.Invoke();
        }

        public int IndexOf(PhotoData p) => p == null ? -1 : _photos.IndexOf(p);
        public bool Contains(PhotoData p) => IndexOf(p) >= 0;

        public bool Remove(PhotoData p)
        {
            int i = IndexOf(p);
            if (i < 0) return false;
            RemoveAt(i);
            return true;
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= _photos.Count) return;
            _photos.RemoveAt(index);
            if (_photos.Count == 0) _selected = -1;
            else if (index < _selected || _selected >= _photos.Count) _selected = Mathf.Max(0, _selected - 1);
            Changed?.Invoke();
        }

        /// <summary>
        /// Replaces the whole list (same photo references, same order) and the selection: checkpoint
        /// restores. Nulls and duplicates are skipped. Raises <see cref="Changed"/> once.
        /// </summary>
        public void SetAll(IReadOnlyList<PhotoData> photos, int selected)
        {
            _photos.Clear();
            if (photos != null)
                for (int i = 0; i < photos.Count; i++)
                    if (photos[i] != null && !_photos.Contains(photos[i])) _photos.Add(photos[i]);
            _selected = _photos.Count == 0 ? -1 : Mathf.Clamp(selected < 0 ? 0 : selected, 0, _photos.Count - 1);
            Changed?.Invoke();
        }

        public void Clear()
        {
            if (_photos.Count == 0) return;
            _photos.Clear();
            _selected = -1;
            Changed?.Invoke();
        }
    }
}
