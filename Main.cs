using MelonLoader;
using HarmonyLib;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using UnityEngine;
using System.Collections.Concurrent;

[assembly: MelonInfo(typeof(ThunderstoreFix.Main), "ThunderstoreFix", "1.0.0", "AKA-Twizzler")]
[assembly: MelonColor(0, 255, 0, 255)]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]

namespace ThunderstoreFix
{
    public class Main : MelonMod
    {
        public override void OnInitializeMelon()
        {
            try
            {
                HarmonyInstance.Patch(
                    AccessTools.Method("ThunderstoreModAssistant.Utilities.ThumbnailThreader:DownloadThumbnail"),
                    prefix: new HarmonyMethod(typeof(Main).GetMethod(nameof(DownloadThumbnailPrefix), 
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic))
                );
                MelonLogger.Msg("ThunderstoreFix: Patched DownloadThumbnail successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"ThunderstoreFix: Failed to patch: {ex.Message}");
            }
        }

        public override void OnUpdate()
        {
            MainThreadDispatcher.ExecuteQueuedActions();
        }

        private static bool DownloadThumbnailPrefix(string url, Action<Texture> action)
        {
            if (string.IsNullOrEmpty(url))
            {
                MelonLogger.Warning("ThunderstoreFix: null/empty URL");
                return false;
            }
            
            MelonLogger.Msg($"ThunderstoreFix: Downloading thumbnail (IPv4 forced): {url}");
            
            Task.Run(async () =>
            {
                try
                {
                    var handler = new SocketsHttpHandler
                    {
                        ConnectCallback = async (context, cancellationToken) =>
                        {
                            var hostEntry = await System.Net.Dns.GetHostEntryAsync(context.DnsEndPoint.Host, cancellationToken);
                            var ipv4 = hostEntry.AddressList.First(ip => ip.AddressFamily == AddressFamily.InterNetwork);
                            var socket = new Socket(ipv4.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                            {
                                NoDelay = true
                            };
                            await socket.ConnectAsync(new IPEndPoint(ipv4, context.DnsEndPoint.Port), cancellationToken);
                            return new NetworkStream(socket, true);
                        },
                        ConnectTimeout = TimeSpan.FromSeconds(15)
                    };
                    
                    using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) })
                    {
                        client.DefaultRequestHeaders.UserAgent.ParseAdd("ThunderstoreFix/1.0.0");
                        byte[] bytes = await client.GetByteArrayAsync(url);
                        
                        MelonLogger.Msg($"ThunderstoreFix: Downloaded {bytes.Length} bytes, dispatching to main thread...");

                        MainThreadDispatcher.QueueAction(() =>
                        {
                            try
                            {
                                var texture = new Texture2D(2, 2);
                                if (ImageConversion.LoadImage(texture, bytes))
                                {
                                    action(texture);
                                    MelonLogger.Msg($"ThunderstoreFix: Texture {texture.width}x{texture.height} applied");
                                }
                                else
                                {
                                    MelonLogger.Error("ThunderstoreFix: ImageConversion.LoadImage returned false");
                                }
                            }
                            catch (Exception ex)
                            {
                                MelonLogger.Error($"ThunderstoreFix: Texture creation error: {ex.Message}");
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"ThunderstoreFix: Download failed: {ex.Message}");
                }
            });
            
            return false; // Skip the original UnityWebRequestTexture method
        }
    }

    /// <summary>
    /// Queues actions to execute on the Unity main thread via MelonMod.OnUpdate().
    /// BoneLib v3.2.1 does not expose a public MainThreadManager, so we provide our own.
    /// </summary>
    public static class MainThreadDispatcher
    {
        private static readonly ConcurrentQueue<Action> _actions = new();

        public static void QueueAction(Action action)
        {
            if (action == null) return;
            _actions.Enqueue(action);
        }

        public static void ExecuteQueuedActions()
        {
            while (_actions.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    MelonLoader.MelonLogger.Error($"MainThreadDispatcher: {ex.Message}");
                }
            }
        }
    }
}
