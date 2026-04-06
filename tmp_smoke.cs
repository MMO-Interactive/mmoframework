using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using MMONetworking;

class Smoke
{
    static async Task<int> Main()
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", 7000);
            await using var stream = client.GetStream();
            var hello = new ClientHelloMessage(WireProtocol.CurrentProtocolVersion, "smoke-user", "smoke-pass-123", AccountAuthMode.Register, 1);
            await WireProtocol.WriteTcpMessageAsync(stream, hello);
            var response = await WireProtocol.ReadTcpMessageAsync(stream);
            Console.WriteLine(response.GetType().Name);
            switch (response)
            {
                case ErrorMessage error:
                    Console.WriteLine(error.Text);
                    return 2;
                case HelloAcceptedMessage accepted:
                    Console.WriteLine($"zone={accepted.ZoneId} tcp={accepted.ZoneTcpPort} udp={accepted.ZoneUdpPort} player={accepted.PlayerId}");
                    return 0;
                default:
                    Console.WriteLine("Unexpected response");
                    return 3;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return 1;
        }
    }
}
