﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.API.Client
{
    public interface IMusicTrack
    {
        /// <summary>
        /// The name of the track
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Is the track active?
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// The priority of the track.
        /// </summary>
        float Priority { get; }

        /// <summary>
        /// Initialization of the Music Track.
        /// </summary>
        /// <param name="assetManager">the global Asset Manager</param>
        /// <param name="capi">The Core Client API</param>
        void Initialize(IAssetManager assetManager, ICoreClientAPI capi);

        /// <summary>
        /// Should this current track play?
        /// </summary>
        /// <param name="props">Player Properties</param>
        /// <param name="musicEngine">the Music Engine.</param>
        /// <returns>Should we play the current track?</returns>
        bool ShouldPlay(TrackedPlayerProperties props, IMusicEngine musicEngine);

        /// <summary>
        /// Begin playing the current track.
        /// </summary>
        /// <param name="props">Player Properties</param>
        /// <param name="musicEngine">the Music Engine.</param>
        void BeginPlay(TrackedPlayerProperties props, IMusicEngine musicEngine);

        /// <summary>
        /// Is it cool for the current track to continue playing?
        /// </summary>
        /// <param name="dt">Delta Time/Change in time.</param>
        /// <param name="props">Track properties.</param>
        /// <param name="musicEngine">the Music Engine.</param>
        /// <returns>Cool or not cool?</returns>
        bool ContinuePlay(float dt, TrackedPlayerProperties props, IMusicEngine musicEngine);

        /// <summary>
        /// Updates the volume on the current track.
        /// </summary>
        void UpdateVolume();

        /// <summary>
        /// Called when the track to interupted or when Update() returned false. 
        /// So called every time the tracked ended or has to end
        /// </summary>
        /// <param name="seconds"></param>
        /// <param name="onFadedOut"></param>
        void FadeOut(float seconds, Common.Action onFadedOut = null);
    }
}
