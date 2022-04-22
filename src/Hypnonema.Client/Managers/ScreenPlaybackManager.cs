﻿using Hypnonema.Client.Extensions;
using Hypnonema.Shared.Communications;

namespace Hypnonema.Client
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    using CitizenFX.Core;

    using Hypnonema.Client.Communications;
    using Hypnonema.Client.Dui;
    using Hypnonema.Client.Graphics;
    using Hypnonema.Client.Players;
    using Hypnonema.Client.Utils;
    using Hypnonema.Shared;
    using Hypnonema.Shared.Models;

    using Newtonsoft.Json;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

    public sealed class ScreenPlaybackManager : IDisposable
    {
        private readonly IList<IMediaPlayer> videoPlayers = new List<IMediaPlayer>();

        ~ScreenPlaybackManager()
        {
            this.Dispose();
        }

        public NetworkMethod<DeleteScreenMessage> DeleteScreen { get; private set; }

        public NetworkMethod<EditScreenMessage> EditScreen { get; private set; }

        public bool IsInitialized { get; private set; }

        public NetworkMethod<PauseMessage> Pause { get; private set; }

        public NetworkMethod<PlayMessage> Play { get; private set; }

        public NetworkMethod<PlaybackEndedMessage> PlaybackEnded { get; private set; }

        public NetworkMethod<ResumeMessage> Resume { get; private set; }

        public NetworkMethod<SeekMessage> Seek { get; private set; }

        public NetworkMethod<StateDurationMessage> StateDuration { get; private set; }

        public NetworkMethod<StopMessage> Stop { get; private set; }

        public void Dispose()
        {
            foreach (var player in this.videoPlayers)
            {
                player.Dispose();
            }

            GC.SuppressFinalize(this);
        }

        public void Initialize()
        {
            if (this.IsInitialized) return;

            this.Play = new NetworkMethod<PlayMessage>(Events.Play, this.OnPlay);
            this.Pause = new NetworkMethod<PauseMessage>(Events.Pause, this.OnPause);
            this.Resume = new NetworkMethod<ResumeMessage>(Events.Resume, this.OnResume);
            this.Stop = new NetworkMethod<StopMessage>(Events.Stop, this.OnStop);
            this.Seek = new NetworkMethod<SeekMessage>(Events.Seek, this.OnSeek);
            this.StateDuration = new NetworkMethod<StateDurationMessage>(
                Events.UpdateStateDuration,
                this.OnUpdateStateDuration);
            this.EditScreen = new NetworkMethod<EditScreenMessage>(Events.EditScreen, this.OnEditScreen);
            this.DeleteScreen = new NetworkMethod<DeleteScreenMessage>(Events.DeleteScreen, this.OnDeleteScreen);
            this.PlaybackEnded = new NetworkMethod<PlaybackEndedMessage>(Events.PlaybackEnded, this.OnPlaybackEnded);
            
            ClientScript.Self.RegisterCallback(Events.Play, this.OnPlay);
            ClientScript.Self.RegisterCallback(Events.Pause, this.OnPause);
            ClientScript.Self.RegisterCallback(Events.Resume, this.OnResume);
            ClientScript.Self.RegisterCallback(Events.Stop, this.OnStop);
            ClientScript.Self.RegisterCallback(Events.Seek, this.OnSeek);

            ClientScript.Self.RegisterCallback(Events.UpdateStateDuration, this.OnUpdateStateDuration);
            ClientScript.Self.RegisterCallback(Events.RequestState, this.OnRequestState);
            ClientScript.Self.RegisterCallback(Events.PlaybackEnded, this.OnPlaybackEnded);

            this.IsInitialized = true;
        }

        public async Task SynchronizeState(DuiState state)
        {
            var player = this.videoPlayers?.FirstOrDefault(s => s.PlayerName == state.ScreenName);
            if (player != null)
            {
                // no need to synchronize. player already exists.
                return;
            }

            player = await this.CreateVideoPlayer(state.Screen);

            player?.SynchronizeState(state.IsPaused, state.CurrentTime, state.CurrentSource, state.Repeat);

            this.videoPlayers?.Add(player);
        }

        private async Task<IMediaPlayer> CreateVideoPlayer(Screen screen)
        {
            IMediaPlayer videoPlayer;

            var duiBrowser = await DuiBrowserPool.Instance.AcquireDuiBrowser(screen);

            if (screen.Is3DRendered)
            {
                var scaleform = await ScaleformRendererPool.Instance.AcquireScaleformRenderer(
                                    screen.PositionalSettings,
                                    duiBrowser.TxdName,
                                    duiBrowser.TxnName);

                videoPlayer = new MediaPlayer3D(
                    scaleform,
                    duiBrowser,
                    screen.Name,
                    screen.BrowserSettings.GlobalVolume,
                    screen.BrowserSettings.SoundAttenuation,
                    screen.BrowserSettings.SoundMaxDistance,
                    screen.BrowserSettings.SoundMinDistance);
            }
            else
            {
                var renderTarget = new RenderTargetRenderer(
                    screen.TargetSettings.ModelName,
                    screen.TargetSettings.RenderTargetName,
                    duiBrowser.TxdName,
                    duiBrowser.TxnName);

                videoPlayer = new MediaPlayer2D(
                    renderTarget,
                    duiBrowser,
                    screen.Name,
                    screen.BrowserSettings.GlobalVolume,
                    screen.BrowserSettings.SoundAttenuation,
                    screen.BrowserSettings.SoundMaxDistance,
                    screen.BrowserSettings.SoundMinDistance);
            }

            return videoPlayer;
        }

        private void OnDeleteScreen(DeleteScreenMessage deleteScreenMessage)
        {
            var player = this.videoPlayers?.FirstOrDefault(p => p.PlayerName == deleteScreenMessage.ScreenName);
            if (player == null) return;

            this.videoPlayers.Remove(player);
            player.Dispose();
        }

        private async void OnEditScreen(EditScreenMessage editScreenMessage)
        {
            var currentPlayer = this.videoPlayers?.FirstOrDefault(p => p.PlayerName == editScreenMessage.Screen.Name);
            if (currentPlayer == null)
            {
                return;
            }

            this.videoPlayers?.Remove(currentPlayer);
            currentPlayer.Dispose();

            var newPlayer = await this.CreateVideoPlayer(editScreenMessage.Screen);

            this.videoPlayers?.Add(newPlayer);
        }

        private void OnPause(IDictionary<string, object> data)
        {
            var screenName = data.GetTypedValue<string>("screenName");
            if (string.IsNullOrEmpty(screenName))
            {
                ClientScript.AddChatMessage("Failed to pause: screenName is missing");
                return;
            }

            var pauseMessage = new PauseMessage(screenName);
            
            this.Pause.Invoke(pauseMessage);
        }

        private void OnPause(PauseMessage pauseMessage)
        {
            var player = this.videoPlayers.FirstOrDefault(p => p.PlayerName == pauseMessage.ScreenName);

            player?.Pause();
        }


        private void OnPlay(IDictionary<string, object> data)
        {
            var videoUrl = data.GetTypedValue<string>("videoUrl");
            var screen = data.GetTypedValue<Screen>("screen");

            if (string.IsNullOrEmpty(videoUrl))
            {
                ClientScript.AddChatMessage("Failed to play: videoUrl is empty");
                return;
            }

            if (screen == null)
            {
                ClientScript.AddChatMessage("Failed to play: missing screen data");
                return;
            }

            var playEvent = new PlayMessage() { Screen = screen, Url = videoUrl };

            this.Play.Invoke(playEvent);
        }


        private async void OnPlay(PlayMessage playMessage)
        {
            var player = this.videoPlayers.FirstOrDefault(p => p.PlayerName == playMessage.Screen.Name);

            if (player == null)
            {
                player = await this.CreateVideoPlayer(playMessage.Screen);
                if (player == null)
                {
                    ClientScript.AddChatMessage("Playback failed. Check logs for more information.");
                    return;
                }

                player.InitDuiBrowser();
                player.Play(playMessage.Url);

                await BaseScript.Delay(500);

                this.videoPlayers.Add(player);

                Logger.Debug($"created a new player for screen: \"{playMessage.Screen.Name}\"");
                return;
            }

            player.Play(playMessage.Url);
        }

        private void OnPlaybackEnded(PlaybackEndedMessage playbackEndedMessage)
        {
            // Nothing to do on client side
        }

        private void OnPlaybackEnded(IDictionary<string, object> data)
        {
            var screenName = data.GetTypedValue<string>("screenName");
            if (string.IsNullOrEmpty(screenName))
            {
                Logger.Error("Failed to process event \"OnPlaybackEnded\" because no screenName was provided.");
                return;
            }

            var player = this.videoPlayers?.FirstOrDefault(p => p.PlayerName == screenName);
            if (player == null)
            {
                return;
            }

            this.videoPlayers.Remove(player);
            player.Dispose();

            var playbackEndedMessage = new PlaybackEndedMessage(screenName);

            this.PlaybackEnded.Invoke(playbackEndedMessage);
        }

        private async Task<List<DuiState>> OnRequestState()
        {
            var state = await ClientScript.Self.DuiStateHelper.RequestDuiStateAsync();

            return state;
        }

        private void OnResume(ResumeMessage resumeMessage)
        {
            var player = this.videoPlayers.FirstOrDefault(p => p.PlayerName == resumeMessage.ScreenName);

            player?.Resume();
        }

        private void OnResume(IDictionary<string, object> data)
        {
            var screenName = data.GetTypedValue<string>("screenName");
            if (string.IsNullOrEmpty(screenName))
            {
                ClientScript.AddChatMessage("Failed to resume. ScreenName is missing");
                return;
            }

            var resumeMessage = new ResumeMessage(screenName);
            
            this.Resume.Invoke(resumeMessage);
        }

        private void OnSeek(IDictionary<string, object> data)
        {
            var screenName = data.GetTypedValue<string>("screenName");
            if (string.IsNullOrEmpty(screenName))
            {
                ClientScript.AddChatMessage("Failed to seek. screenName is missing!");
                return;
            }

            var time = data.GetTypedValue<float>("time");

            var seekMessage = new SeekMessage(screenName, time);

            this.Seek.Invoke(seekMessage);
        }

        private void OnSeek(SeekMessage seekMessage)
        {
            var player = this.videoPlayers.FirstOrDefault(s => s.PlayerName == seekMessage.ScreenName);

            player?.Seek(seekMessage.Time);
        }

        private void OnStop(IDictionary<string, object> data)
        {
            var screenName = data.GetTypedValue<string>("screenName");
            if (string.IsNullOrEmpty(screenName))
            {
                ClientScript.AddChatMessage("Failed to stop. screenName is missing");
                return;
            }

            var stopMessage = new StopMessage(screenName);

            this.Stop.Invoke(stopMessage);
        }

        private void OnStop(StopMessage stopMessage)
        {
            var player = this.videoPlayers.FirstOrDefault(p => p.PlayerName == stopMessage.ScreenName);
            if (player == null) return;

            player.Stop();

            this.videoPlayers.Remove(player);

            player.Dispose();
        }

        private void OnUpdateStateDuration(StateDurationMessage updateStateDurationMessage)
        {
            // nothing to do on client side
        }

        private void OnUpdateStateDuration(IDictionary<string, object> data)
        {
            var screenName = data.GetTypedValue<string>("screenName");
            var duration = data.GetTypedValue<float>("duration");

            if (string.IsNullOrEmpty(screenName))
            {
                ClientScript.AddChatMessage("Failed to update state. screenName is missing");
                return;
            }

            var stateDurationMessage = new StateDurationMessage(screenName, duration);

            this.StateDuration.Invoke(stateDurationMessage);
        }
    }
}