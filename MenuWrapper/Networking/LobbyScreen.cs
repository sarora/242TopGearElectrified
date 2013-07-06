#region File Description
//-----------------------------------------------------------------------------
// LobbyScreen.cs
//
// Microsoft XNA Community Game Platform
// Copyright (C) Microsoft Corporation. All rights reserved.
//-----------------------------------------------------------------------------
#endregion

#region Using Statements
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Net;
using GameEngine.Network;
#endregion

namespace NetworkStateManagement
{

    /// <summary>
    /// Purpose: To extend the NetworkGamer to allow storage of number of local players (in the tag)
    /// </summary>
    public static class NetworkGamerExtension
    {
        const int MAX_LOCAL_PLAYERS = 2;
        const int MIN_LOCAL_PLAYERS = 1;

        /// <summary>
        /// Get the local player count from the gamer, or if not set, return the default
        /// </summary>
        public static int GetLocalPlayerCount(this NetworkGamer gamer)
        {
            if (gamer.Tag == null) return MIN_LOCAL_PLAYERS;
            else return (int)gamer.Tag;
        }

        /// <summary>
        /// Set the local player count for the gamer to change by change (can be positive or negative).
        /// Any change past the min or max values is wrapped around (not clamped).
        /// </summary>
        public static void SetLocalPlayerCount(this NetworkGamer gamer, int change)
        {
            int newPlayerCount = gamer.GetLocalPlayerCount() + change;

            // wrap player count around (e.g. if currently on max players, an addition of one will result in being set to min players)
            if (newPlayerCount > MAX_LOCAL_PLAYERS) newPlayerCount = MIN_LOCAL_PLAYERS;
            else if (newPlayerCount < MIN_LOCAL_PLAYERS) newPlayerCount = MAX_LOCAL_PLAYERS;

            gamer.Tag = newPlayerCount;
        }

        /// <summary>
        /// Send the local gamer's local player count to network.
        /// </summary>
        public static void SendToNetwork(this LocalNetworkGamer localGamer)
        {
            PacketWriter writer = new PacketWriter();
            writer.Write(PacketHeader.LOBBY_DATA);
            writer.Write(localGamer.GetLocalPlayerCount());

            // send in order, since old data must not overwrite the new data.
            localGamer.SendData(writer, SendDataOptions.InOrder);
        }

        /// <summary>
        /// Update the sender's local player count from the network. 
        /// </summary>
        public static void UpdateFromNetwork(this NetworkGamer sender, PacketReader reader)
        {
            // ignore any packets with the wrong header (possibly delayed packets from a previous exchange)
            if (reader.ReadByte() == PacketHeader.LOBBY_DATA) sender.Tag = reader.ReadInt32();
        }
    }

    /// <summary>
    /// The lobby screen provides a place for gamers to congregate before starting
    /// the actual gameplay. It displays a list of all the gamers in the session,
    /// and indicates which ones are currently talking. Each gamer can press a button
    /// to mark themselves as ready: gameplay will begin after everyone has done this.
    /// </summary>
    class LobbyScreen : GameScreen
    {
        
        #region Fields

        NetworkSession networkSession;
        
        LocalNetworkGamer localGamer;

        Texture2D isReadyTexture;
        Texture2D hasVoiceTexture;
        Texture2D isTalkingTexture;
        Texture2D voiceMutedTexture;

        #endregion

        #region Initialization


        /// <summary>
        /// Constructs a new lobby screen.
        /// </summary>
        public LobbyScreen(NetworkSession networkSession)
        {
            this.networkSession = networkSession;
            localGamer = networkSession.LocalGamers[0];
            localGamer.SetLocalPlayerCount(0);

            TransitionOnTime = TimeSpan.FromSeconds(0.5);
            TransitionOffTime = TimeSpan.FromSeconds(0.5);
        }


        /// <summary>
        /// Loads graphics content used by the lobby screen.
        /// </summary>
        public override void LoadContent()
        {
            ContentManager content = ScreenManager.Game.Content;

            isReadyTexture = content.Load<Texture2D>("chat_ready");
            hasVoiceTexture = content.Load<Texture2D>("chat_able");
            isTalkingTexture = content.Load<Texture2D>("chat_talking");
            voiceMutedTexture = content.Load<Texture2D>("chat_mute");
        }


        #endregion

        
        #region Update

        /// <summary>
        /// Updates the lobby screen.
        /// </summary>
        public override void Update(GameTime gameTime, bool otherScreenHasFocus,
                                                       bool coveredByOtherScreen)
        {
            base.Update(gameTime, otherScreenHasFocus, coveredByOtherScreen);

            if (!IsExiting)
            {
                if (networkSession.SessionState == NetworkSessionState.Playing)
                {
                    // Check if we should leave the lobby and begin gameplay.
                    // We pass null as the controlling player, because the networked
                    // gameplay screen accepts input from any local players who
                    // are in the session, not just a single controlling player.
                    LoadingScreen.Load(ScreenManager, true, null,
                                       new GameplayScreen(networkSession));
                }
                else if (networkSession.IsHost && networkSession.IsEveryoneReady)
                {
                    // The host checks whether everyone has marked themselves
                    // as ready, and starts the game in response.
                    networkSession.StartGame();
                }
            }

            if (!localGamer.HasLeftSession)
            {
                while (localGamer.IsDataAvailable)
                {
                    PacketReader reader = new PacketReader();
                    NetworkGamer sender;
                    localGamer.ReceiveData(reader, out sender);

                    // ignore packets from self
                    if (sender.Id != localGamer.Id) sender.UpdateFromNetwork(reader);
                }

                // broadcast current data to everyone.
                localGamer.SendToNetwork();
            }
        }


        /// <summary>
        /// Handles user input for all the local gamers in the session. Unlike most
        /// screens, which use the InputState class to combine input data from all
        /// gamepads, the lobby needs to individually mark specific players as ready,
        /// so it loops over all the local gamers and reads their inputs individually.
        /// </summary>
        public override void HandleInput(InputState input)
        {
            foreach (LocalNetworkGamer gamer in networkSession.LocalGamers)
            {
                PlayerIndex playerIndex = gamer.SignedInGamer.PlayerIndex;

                PlayerIndex unwantedOutput;

                if (input.IsMenuSelect(playerIndex, out unwantedOutput))
                {
                    HandleMenuSelect(gamer);
                }
                else if (input.IsMenuCancel(playerIndex, out unwantedOutput))
                {
                    HandleMenuCancel(gamer);
                }
                else if (input.IsMenuUp(playerIndex)) localGamer.SetLocalPlayerCount(1);
                else if (input.IsMenuDown(playerIndex)) localGamer.SetLocalPlayerCount(-1);
            }
        }


        /// <summary>
        /// Handle MenuSelect inputs by marking ourselves as ready.
        /// </summary>
        void HandleMenuSelect(LocalNetworkGamer gamer)
        {
            if (!gamer.IsReady)
            {
                gamer.IsReady = true;
            }
            else if (gamer.IsHost)
            {
                // The host has an option to force starting the game, even if not
                // everyone has marked themselves ready. If they press select twice
                // in a row, the first time marks the host ready, then the second
                // time we ask if they want to force start.
                MessageBoxScreen messageBox = new MessageBoxScreen(
                                                    Resources.ConfirmForceStartGame);

                messageBox.Accepted += ConfirmStartGameMessageBoxAccepted;

                ScreenManager.AddScreen(messageBox, gamer.SignedInGamer.PlayerIndex);
            }
        }


        /// <summary>
        /// Event handler for when the host selects ok on the "are you sure
        /// you want to start even though not everyone is ready" message box.
        /// </summary>
        void ConfirmStartGameMessageBoxAccepted(object sender, PlayerIndexEventArgs e)
        {
            if (networkSession.SessionState == NetworkSessionState.Lobby)
            {
                networkSession.StartGame();
            }
        }


        /// <summary>
        /// Handle MenuCancel inputs by clearing our ready status, or if it is
        /// already clear, prompting if the user wants to leave the session.
        /// </summary>
        void HandleMenuCancel(LocalNetworkGamer gamer)
        {
            if (gamer.IsReady)
            {
                gamer.IsReady = false;
            }
            else
            {
                PlayerIndex playerIndex = gamer.SignedInGamer.PlayerIndex;

                NetworkSessionComponent.LeaveSession(ScreenManager, playerIndex);
            }
        }


        #endregion

        #region Draw


        /// <summary>
        /// Draws the lobby screen.
        /// </summary>
        public override void Draw(GameTime gameTime)
        {
            SpriteBatch spriteBatch = ScreenManager.SpriteBatch;
            SpriteFont font = ScreenManager.Font;

            Vector2 position = new Vector2(100, 150);

            // Make the lobby slide into place during transitions.
            float transitionOffset = (float)Math.Pow(TransitionPosition, 2);

            if (ScreenState == ScreenState.TransitionOn)
                position.X -= transitionOffset * 256;
            else
                position.X += transitionOffset * 512;

            spriteBatch.Begin();

            // Draw all the gamers in the session.
            int gamerCount = 0;

            foreach (NetworkGamer gamer in networkSession.AllGamers)
            {
                DrawGamer(gamer, position);

                // Advance to the next screen position, wrapping into two
                // columns if there are more than 8 gamers in the session.
                if (++gamerCount == 8)
                {
                    position.X += 433;
                    position.Y = 150;
                }
                else
                    position.Y += ScreenManager.Font.LineSpacing;
            }

            // Draw the screen title.
            string title = Resources.Lobby;

            Vector2 titlePosition = new Vector2(533, 80);
            Vector2 titleOrigin = font.MeasureString(title) / 2;
            Color titleColor = new Color(192, 192, 192, TransitionAlpha);
            float titleScale = 1.25f;

            titlePosition.Y -= transitionOffset * 100;

            spriteBatch.DrawString(font, title, titlePosition, titleColor, 0,
                                   titleOrigin, titleScale, SpriteEffects.None, 0);

            spriteBatch.End();
        }


        /// <summary>
        /// Helper draws the gamertag and status icons for a single NetworkGamer.
        /// </summary>
        void DrawGamer(NetworkGamer gamer, Vector2 position)
        {
            SpriteBatch spriteBatch = ScreenManager.SpriteBatch;
            SpriteFont font = ScreenManager.Font;

            Vector2 iconWidth = new Vector2(34, 0);
            Vector2 iconOffset = new Vector2(0, 12);

            Vector2 iconPosition = position + iconOffset;

            // Draw the "is ready" icon.
            if (gamer.IsReady)
            {
                spriteBatch.Draw(isReadyTexture, iconPosition,
                                 FadeAlphaDuringTransition(Color.Lime));
            }

            iconPosition += iconWidth;

            // Draw the "is muted", "is talking", or "has voice" icon.
            if (gamer.IsMutedByLocalUser)
            {
                spriteBatch.Draw(voiceMutedTexture, iconPosition,
                                 FadeAlphaDuringTransition(Color.Red));
            }
            else if (gamer.IsTalking)
            {
                spriteBatch.Draw(isTalkingTexture, iconPosition,
                                 FadeAlphaDuringTransition(Color.Yellow));
            }
            else if (gamer.HasVoice)
            {
                spriteBatch.Draw(hasVoiceTexture, iconPosition,
                                 FadeAlphaDuringTransition(Color.White));
            }

            // Draw the gamertag, normally in white, but yellow for local players.
            string text = gamer.Gamertag;

            if (gamer.IsHost)
                text += Resources.HostSuffix;

            text += " (" + gamer.GetLocalPlayerCount() + " players)";

            Color color = (gamer.IsLocal) ? Color.Yellow : Color.White;

            spriteBatch.DrawString(font, text, position + iconWidth * 2,
                                   FadeAlphaDuringTransition(color));
        }


        /// <summary>
        /// Helper modifies a color to fade its alpha value during screen transitions.
        /// </summary>
        Color FadeAlphaDuringTransition(Color color)
        {
            return new Color(color.R, color.G, color.B, TransitionAlpha);
        }


        #endregion
    }

    
}
