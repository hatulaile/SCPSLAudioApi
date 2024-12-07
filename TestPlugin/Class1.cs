using System;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using AdminToys;
using CommandSystem;
using GameCore;
using InventorySystem.Items.Autosync;
using InventorySystem.Items.Firearms;
using InventorySystem.Items.Firearms.Attachments;
using InventorySystem.Items.Firearms.Attachments.Components;
using InventorySystem.Items.Firearms.Modules;
using Mirror;
using NorthwoodLib.Pools;
using PluginAPI.Core;
using PluginAPI.Core.Attributes;
using PluginAPI.Events;
using SCPSLAudioApi.AudioCore;
using UnityEngine;
using UserSettings.ServerSpecific;
using Console = System.Console;
using Log = PluginAPI.Core.Log;

namespace ScpslNwapiTest;

public sealed class Config
{
    public string MusicPath { get; set; }
}

public sealed class Plugin
{
    [PluginEntryPoint("TestPlugin", "0.0.0.1", "TestPlugin", "hatu")]
    public void LoadPlugin()
    {
        //EventManager.RegisterAllEvents(this);
    }

    [PluginConfig] public static Config Config;
}

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
public sealed class TestCommand : ICommand
{
    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        Player player = Player.Get(sender);
        if (player == null)
        {
            StringBuilder sb = StringBuilderPool.Shared.Rent();
            foreach (var value in NetworkServer.spawned.Values)
            {
                if (!value.TryGetComponent<SpeakerToy>(out var toy)) continue;
                sb.Append(toy.NetworkControllerId);
            }

            response = StringBuilderPool.Shared.ToStringReturn(sb);
            return true;
        }

        SpawnSpeakerToy(player.Position, float.Parse(arguments.At(0)));
        response = "Nice";
        return true;
    }

    public static void SpawnSpeakerToy(Vector3 position, float volume = 1f)
    {
        var audio = AudioSpeakerBase.Create();
        Log.Info(audio.SpeakerToy.ControllerId.ToString());
        audio.Position = position;
        audio.IsSpatial = true;
        audio.AllowUrl = true;
        audio.Volume = volume;
        audio.LogInfo = true;
        audio.LogDebug = false;

        audio.Enqueue(Plugin.Config.MusicPath, -1);
        audio.Play(0);
    }

    public string Command { get; } = "playtest";
    public string[] Aliases { get; } = ["t","pt"];
    public string Description { get; } = "Use Speaker Toy to play test Music";
}