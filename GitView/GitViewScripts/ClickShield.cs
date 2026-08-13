using System.Collections.Generic;
using UnityEngine;

namespace GitView
{
    /// <summary>
    /// Stops the game being clicked through one of this mod's windows.
    ///
    /// Besiege's own popups and buttons are 3D objects with colliders: they all
    /// come from <c>ClickBehaviour</c>, which answers Unity's <c>OnMouseOver</c>.
    /// Those messages are raycast from the cameras and know nothing about uGUI, so
    /// a canvas drawn over one hides it without stopping it being pressed -- the
    /// "this machine uses keys also used by your general control scheme" warning
    /// takes a click aimed at the list drawn on top of it.
    ///
    /// <c>Camera.eventMask</c> is what does stop it. Zeroed while the pointer is
    /// inside one of our windows, no collider in the game is offered the click;
    /// put back the moment the pointer leaves, so nothing about the game is
    /// changed except while it is covered.
    /// </summary>
    public class ClickShield
    {
        private readonly List<RectTransform> _windows = new List<RectTransform>();

        // The cameras currently held down, and the mask each had before.
        private readonly List<Camera> _held = new List<Camera>();
        private readonly List<int> _masks = new List<int>();

        private Camera[] _all = new Camera[0];
        private bool _up;

        /// <summary>Adds a window the game may not be clicked through.</summary>
        public void Guard(RectTransform window)
        {
            if (window != null && !_windows.Contains(window))
            {
                _windows.Add(window);
            }
        }

        /// <summary>Raises or drops the shield to match where the pointer is.</summary>
        public void Follow()
        {
            if (Covered())
            {
                Hold();
                _up = true;
            }
            else if (_up)
            {
                Release();
            }
        }

        /// <summary>
        /// Puts every camera back as it was. Called when the pointer leaves and
        /// again when the mod's UI goes away, since a shield left up is a game
        /// whose own buttons have stopped answering.
        /// </summary>
        public void Release()
        {
            for (int i = 0; i < _held.Count; i++)
            {
                if (_held[i] != null)
                {
                    _held[i].eventMask = _masks[i];
                }
            }
            _held.Clear();
            _masks.Clear();
            _up = false;
        }

        private bool Covered()
        {
            Vector2 at = Input.mousePosition;
            for (int i = 0; i < _windows.Count; i++)
            {
                RectTransform window = _windows[i];
                // Null camera: these are all on a screen-space overlay canvas,
                // where the screen point is already the point on the canvas.
                if (window != null && window.gameObject.activeInHierarchy &&
                    RectTransformUtility.RectangleContainsScreenPoint(window, at, null))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Takes every camera's event mask down. Gathered each frame rather than
        /// once: cameras come and go with the scene, and one built while the
        /// shield is up would otherwise be the one hole in it.
        /// </summary>
        private void Hold()
        {
            int count = Camera.allCamerasCount;
            if (_all.Length < count)
            {
                _all = new Camera[count];
            }
            Camera.GetAllCameras(_all);

            for (int i = 0; i < count; i++)
            {
                Camera camera = _all[i];
                if (camera == null || _held.Contains(camera))
                {
                    continue;
                }
                // What goes back is what the game had, not what a camera usually
                // has: a mask is a set of layers and the game picks its own.
                _held.Add(camera);
                _masks.Add(camera.eventMask);
                camera.eventMask = 0;
            }
        }
    }
}
