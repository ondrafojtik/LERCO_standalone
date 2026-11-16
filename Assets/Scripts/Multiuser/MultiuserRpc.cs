using System;
using Fusion;

// The only place where the app talks to Photon Fusion directly.
// A static RPC needs no NetworkObject in the scene: every client in the session receives the bytes.
public class MultiuserRpc : SimulationBehaviour
{
    public static event Action<int, byte[]> ChunkReceived;

    [Rpc(Channel = RpcChannel.Reliable, InvokeLocal = false, TickAligned = false)]
    public static void Rpc_Chunk(NetworkRunner runner, int senderId, byte[] data)
    {
        if (ChunkReceived != null) ChunkReceived(senderId, data);
    }
}
