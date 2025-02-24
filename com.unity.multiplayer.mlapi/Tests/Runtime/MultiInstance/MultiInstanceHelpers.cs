using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MLAPI.Configuration;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MLAPI.RuntimeTests
{
    /// <summary>
    /// Provides helpers for running multi instance tests.
    /// </summary>
    internal static class MultiInstanceHelpers
    {
        /// <summary>
        /// Creates NetworkingManagers and configures them for use in a multi instance setting.
        /// </summary>
        /// <param name="clientCount">The amount of clients</param>
        /// <param name="server">The server NetworkManager</param>
        /// <param name="clients">The clients NetworkManagers</param>
        public static bool Create(int clientCount, out NetworkManager server, out NetworkManager[] clients)
        {
            clients = new NetworkManager[clientCount];

            for (int i = 0; i < clientCount; i++)
            {
                // Create gameObject
                var go = new GameObject("NetworkManager - Client - " + i);

                // Create networkManager component
                clients[i] = go.AddComponent<NetworkManager>();

                // Set config
                clients[i].NetworkConfig = new NetworkConfig()
                {
                    // Set the current scene to prevent unexpected log messages which would trigger a failure
                    RegisteredScenes = new List<string>() { SceneManager.GetActiveScene().name },
                    // Set transport
                    NetworkTransport = go.AddComponent<SIPTransport>()
                };
            }

            {
                // Create gameObject
                var go = new GameObject("NetworkManager - Server");

                // Create networkManager component
                server = go.AddComponent<NetworkManager>();

                // Set config
                server.NetworkConfig = new NetworkConfig()
                {
                    // Set the current scene to prevent unexpected log messages which would trigger a failure
                    RegisteredScenes = new List<string>() { SceneManager.GetActiveScene().name },
                    // Set transport
                    NetworkTransport = go.AddComponent<SIPTransport>()
                };
            }

            return true;
        }

        /// <summary>
        /// Starts NetworkManager instances created by the Create method.
        /// </summary>
        /// <param name="host">Whether or not to create a Host instead of Server</param>
        /// <param name="server">The Server NetworkManager</param>
        /// <param name="clients">The Clients NetworkManager</param>
        public static bool Start(bool host, NetworkManager server, NetworkManager[] clients)
        {
            if (host)
            {
                server.StartHost();
            }
            else
            {
                server.StartClient();
            }

            for (int i = 0; i < clients.Length; i++)
            {
                clients[i].StartClient();
            }

            return true;
        }

        // Empty MonoBehaviour that is a holder of coroutine
        private class CoroutineRunner : MonoBehaviour
        {
        }

        private static CoroutineRunner s_CoroutineRunner;

        /// <summary>
        /// Runs a IEnumerator as a Coroutine on a dummy GameObject.
        /// </summary>
        /// <param name="enumerator">The IEnumerator to run</param>
        public static Coroutine Run(IEnumerator enumerator)
        {
            if (s_CoroutineRunner == null)
            {
                s_CoroutineRunner = new GameObject(nameof(CoroutineRunner)).AddComponent<CoroutineRunner>();
            }

            return s_CoroutineRunner.StartCoroutine(enumerator);
        }

        public class CoroutineResultWrapper<T>
        {
            public T Result;
        }

        /// <summary>
        /// Normally we would only allow player prefabs to be set to a prefab. Not runtime created objects.
        /// In order to prevent having a Resource folder full of a TON of prefabs that we have to maintain,
        /// MultiInstanceHelper has a helper function that lets you mark a runtime created object to be
        /// treated as a prefab by the MLAPI. That's how we can get away with creating the player prefab
        /// at runtime without it being treated as a SceneObject or causing other conflicts with the MLAPI.
        /// </summary>
        /// <param name="networkObject">The networkObject to be treated as Prefab</param>
        /// <param name="globalObjectIdHash">The GlobalObjectId to force</param>
        public static void MakeNetworkedObjectTestPrefab(NetworkObject networkObject, uint globalObjectIdHash = default)
        {
            // Set a globalObjectId for prefab
            if (globalObjectIdHash != default)
            {
                networkObject.TempGlobalObjectIdHashOverride = globalObjectIdHash;
            }

            // Force generation
            networkObject.GenerateGlobalObjectIdHash();

            // Prevent object from being snapped up as a scene object
            networkObject.IsSceneObject = false;
        }

        /// <summary>
        /// Waits on the client side to be connected.
        /// </summary>
        /// <param name="client">The client</param>
        /// <param name="result">The result. If null, it will automatically assert</param>
        /// <param name="maxFrames">The max frames to wait for</param>
        public static IEnumerator WaitForClientConnected(NetworkManager client, CoroutineResultWrapper<bool> result = null, int maxFrames = 64)
        {
            if (client.IsServer)
            {
                throw new InvalidOperationException("Cannot wait for connected as server");
            }

            int startFrame = Time.frameCount;

            while (Time.frameCount - startFrame <= maxFrames && !client.IsConnectedClient)
            {
                int nextFrameId = Time.frameCount + 1;
                yield return new WaitUntil(() => Time.frameCount >= nextFrameId);
            }

            bool res = client.IsConnectedClient;

            if (result != null)
            {
                result.Result = res;
            }
            else
            {
                Assert.True(res, "Client never connected");
            }
        }

        /// <summary>
        /// Waits on the server side for 1 client to be connected
        /// </summary>
        /// <param name="server">The server</param>
        /// <param name="result">The result. If null, it will automatically assert</param>
        /// <param name="maxFrames">The max frames to wait for</param>
        public static IEnumerator WaitForClientConnectedToServer(NetworkManager server, CoroutineResultWrapper<bool> result = null, int maxFrames = 64)
        {
            if (!server.IsServer)
            {
                throw new InvalidOperationException("Cannot wait for connected as client");
            }

            int startFrame = Time.frameCount;

            while (Time.frameCount - startFrame <= maxFrames && server.ConnectedClients.Count != (server.IsHost ? 2 : 1))
            {
                int nextFrameId = Time.frameCount + 1;
                yield return new WaitUntil(() => Time.frameCount >= nextFrameId);
            }

            bool res = server.ConnectedClients.Count == (server.IsHost ? 2 : 1);

            if (result != null)
            {
                result.Result = res;
            }
            else
            {
                Assert.True(res, "Client never connected to server");
            }
        }

        /// <summary>
        /// Gets a NetworkObject instance as it's represented by a certain peer.
        /// </summary>
        /// <param name="networkObjectId">The networkObjectId to get</param>
        /// <param name="representation">The representation to get the object from</param>
        /// <param name="result">The result</param>
        /// <param name="failIfNull">Whether or not to fail if no object is found and result is null</param>
        /// <param name="maxFrames">The max frames to wait for</param>
        public static IEnumerator GetNetworkObjectByRepresentation(ulong networkObjectId, NetworkManager representation, CoroutineResultWrapper<NetworkObject> result, bool failIfNull = true, int maxFrames = 64)
        {
            if (result == null)
            {
                throw new ArgumentNullException("Result cannot be null");
            }

            int startFrame = Time.frameCount;

            while (Time.frameCount - startFrame <= maxFrames && representation.SpawnManager.SpawnedObjects.All(x => x.Value.NetworkObjectId != networkObjectId))
            {
                int nextFrameId = Time.frameCount + 1;
                yield return new WaitUntil(() => Time.frameCount >= nextFrameId);
            }

            result.Result = representation.SpawnManager.SpawnedObjects.First(x => x.Value.NetworkObjectId == networkObjectId).Value;

            if (failIfNull && result.Result == null)
            {
                Assert.Fail("NetworkObject could not be found");
            }
        }

        /// <summary>
        /// Gets a NetworkObject instance as it's represented by a certain peer.
        /// </summary>
        /// <param name="predicate">The predicate used to filter for your target NetworkObject</param>
        /// <param name="representation">The representation to get the object from</param>
        /// <param name="result">The result</param>
        /// <param name="failIfNull">Whether or not to fail if no object is found and result is null</param>
        /// <param name="maxFrames">The max frames to wait for</param>
        public static IEnumerator GetNetworkObjectByRepresentation(Func<NetworkObject, bool> predicate, NetworkManager representation, CoroutineResultWrapper<NetworkObject> result, bool failIfNull = true, int maxFrames = 64)
        {
            if (result == null)
            {
                throw new ArgumentNullException("Result cannot be null");
            }

            if (predicate == null)
            {
                throw new ArgumentNullException("Predicate cannot be null");
            }

            int startFrame = Time.frameCount;

            while (Time.frameCount - startFrame <= maxFrames && !representation.SpawnManager.SpawnedObjects.Any(x => predicate(x.Value)))
            {
                int nextFrameId = Time.frameCount + 1;
                yield return new WaitUntil(() => Time.frameCount >= nextFrameId);
            }

            result.Result = representation.SpawnManager.SpawnedObjects.FirstOrDefault(x => predicate(x.Value)).Value;

            if (failIfNull && result.Result == null)
            {
                Assert.Fail("NetworkObject could not be found");
            }
        }

        /// <summary>
        /// Waits for a predicate condition to be met
        /// </summary>
        /// <param name="predicate">The predicate to wait for</param>
        /// <param name="result">The result. If null, it will fail if the predicate is not met</param>
        /// <param name="maxFrames">The max frames to wait for</param>
        public static IEnumerator WaitForCondition(Func<bool> predicate, CoroutineResultWrapper<bool> result = null, int maxFrames = 64)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException("Predicate cannot be null");
            }

            int startFrame = Time.frameCount;

            while (Time.frameCount - startFrame <= maxFrames && !predicate())
            {
                int nextFrameId = Time.frameCount + 1;
                yield return new WaitUntil(() => Time.frameCount >= nextFrameId);
            }

            bool res = predicate();

            if (result != null)
            {
                result.Result = res;
            }
            else
            {
                Assert.True(res, "PREDICATE CONDITION");
            }
        }
    }
}
