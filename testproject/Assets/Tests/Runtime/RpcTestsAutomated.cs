using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using TestProject.ManualTests;
using MLAPI;
using Debug = UnityEngine.Debug;

namespace TestProject.RuntimeTests
{
    public class RPCTestsAutomated
    {       
        private bool m_TimedOut;
        
        [UnityTest]
        public IEnumerator AutomatedManualRpcTests()
        {
            // Set RpcQueueManualTests into unit testing mode
            RpcQueueManualTests.UnitTesting = true;

            // Create 1 Host and 7 clients
            Assert.True(MultiInstanceHelpers.Create(7, out NetworkManager server, out NetworkManager[] clients));

            // Create a default player GameObject to use
            var playerPrefab = new GameObject("Player");
            var networkObject = playerPrefab.AddComponent<NetworkObject>();

            // Add our RpcQueueManualTests component
            playerPrefab.AddComponent<RpcQueueManualTests>();

            // Make it a prefab
            MultiInstanceHelpers.MakeNetworkedObjectTestPrefab(networkObject);

            // Set the player prefab
            server.NetworkConfig.PlayerPrefab = playerPrefab;

            // Set all of the client's player prefab
            for (int i = 0; i < clients.Length; i++)
            {
                clients[i].NetworkConfig.PlayerPrefab = playerPrefab;
            }

            // Start the instances
            if (!MultiInstanceHelpers.Start(true, server, clients))
            {
                Debug.LogError("Failed to start instances");
                Assert.Fail("Failed to start instances");
            }

            // [Client-Side] Wait for a connection to the server 
            yield return MultiInstanceHelpers.Run(MultiInstanceHelpers.WaitForClientsConnected(clients));

            // [Host-Side] Check to make sure all clients are connected
            yield return MultiInstanceHelpers.Run(MultiInstanceHelpers.WaitForClientsConnectedToServer(server, clients.Length + 1));

            // [Host-Side] Get the Host owned instance of the RpcQueueManualTests
            var serverClientPlayerResult = new MultiInstanceHelpers.CoroutineResultWrapper<NetworkObject>();
            yield return MultiInstanceHelpers.Run(MultiInstanceHelpers.GetNetworkObjectByRepresentation((x => x.IsPlayerObject && x.OwnerClientId == clients[0].LocalClientId), server, serverClientPlayerResult));

            var serverRpcTests = serverClientPlayerResult.Result.GetComponent<RpcQueueManualTests>();
            Assert.IsNotNull(serverRpcTests);

            // [Host-Side] Set the (unit) testing mode
            serverRpcTests.SetTestingMode(true, 1);

            // [Client-Side] Get all of the RpcQueueManualTests instances relative to each client
            var clientRpcQueueManualTestInstsances = new List<RpcQueueManualTests>();
            foreach (var client in clients)
            {
                var clientClientPlayerResult = new MultiInstanceHelpers.CoroutineResultWrapper<NetworkObject>();
                yield return MultiInstanceHelpers.Run(MultiInstanceHelpers.GetNetworkObjectByRepresentation((x => x.IsPlayerObject && x.OwnerClientId == clients[0].LocalClientId), client, clientClientPlayerResult));
                var clientRpcTests = clientClientPlayerResult.Result.GetComponent<RpcQueueManualTests>();
                Assert.IsNotNull(clientRpcTests);
                clientRpcQueueManualTestInstsances.Add(clientRpcTests);
            }

            // [Client-Side] Set the (unit) testing mode for each client
            foreach (var rpcClientSideTest in clientRpcQueueManualTestInstsances)
            {
                rpcClientSideTest.SetTestingMode(true,1);
            }

            // [Host-Side] Begin testing on the host
            serverRpcTests.BeginTest();

            // [Client-Side] Begin testing on each client
            foreach (var rpcClientSideTest in clientRpcQueueManualTestInstsances)
            {                
                rpcClientSideTest.BeginTest();
            }

            // Wait for the all of the tests to finish
            var timeOut = Time.realtimeSinceStartup + 5;
            while (!serverRpcTests.IsFinishedWithTest())
            {
                yield return new WaitForSeconds(0.01f);
                if (timeOut < Time.realtimeSinceStartup)
                {
                    m_TimedOut = true;
                    break;
                }
            }

            // Verify we didn't time out (i.e. all tests ran and finished)
            Assert.IsFalse(m_TimedOut);

            // Log the output for visual confirmation (Acceptance Test for this test) that all Rpc test types (tracked by counters) executed multiple times
            Debug.Log("Final Host-Server Status Info:");
            Debug.Log(serverRpcTests.GetCurrentServerStatusInfo());
            
            foreach (var rpcClientSideTest in clientRpcQueueManualTestInstsances)
            {
                Debug.Log($"Final Client {rpcClientSideTest.NetworkManager.LocalClientId} Status Info:");
                Debug.Log(rpcClientSideTest.GetCurrentClientStatusInfo());
            }

            // Shutdown and clean up both of our NetworkManager instances
            MultiInstanceHelpers.ShutdownAndClean();
        }
    }
}
