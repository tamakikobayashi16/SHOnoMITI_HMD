using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEngine;

static class Program {
    static void Call(RemoteInkReceiver r,string method) { typeof(RemoteInkReceiver).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(r,null); }
    static void Require(bool value,string message) { if(!value) throw new Exception(message); }
    static string Packet(string session,int seq,string type,int character=0,int stroke=0) =>
        $"{{\"version\":1,\"session\":\"{session}\",\"method\":\"PenTablet\",\"eventType\":\"{type}\",\"sequence\":{seq},\"character\":{character},\"stroke\":{stroke},\"x\":1.25,\"y\":-0.5,\"z\":9.5,\"pressure\":0.7}}";
    static void Main() {
        RemoteInkProtocolChecks.Run();
        var receiver=new RemoteInkReceiver();
        using(var probe=new UdpClient(new IPEndPoint(IPAddress.Loopback,0))) receiver.listenPort=((IPEndPoint)probe.Client.LocalEndPoint).Port;
        Call(receiver,"OnEnable");
        Require(receiver.IsListening,"Socket did not start");
        try {
            using var sender=new UdpClient();
            void Send(string packet) { var bytes=Encoding.UTF8.GetBytes(packet); sender.Send(bytes,bytes.Length,new IPEndPoint(IPAddress.Loopback,receiver.listenPort)); }
            Send(Packet("socket",0,"point")); Send(Packet("socket",1,"strokeEnd"));
            Send(Packet("socket",2,"nextCharacter",1)); Send(Packet("socket",3,"point",1)); Send(Packet("socket",4,"experimentEnd",1));
            var deadline=DateTime.UtcNow.AddSeconds(3);
            while(!receiver.HasExperimentEnded && DateTime.UtcNow<deadline) { Thread.Sleep(10); Call(receiver,"Update"); }
            Require(receiver.HasExperimentEnded && !receiver.HasPacketLoss && receiver.Characters.Length==2,"UDP round trip failed");
            var lines=(Dictionary<string,LineRenderer>)typeof(RemoteInkReceiver).GetField("lines",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(receiver);
            Require(lines["0:0"].GetPosition(0).x==1.25f && lines["0:0"].GetPosition(0).z==9.5f,"Coordinate mismatch");
            receiver.TranslateExample(new Vector3(2,3,-1));
            Require(lines["0:0"].GetPosition(0).x==3.25f && lines["0:0"].GetPosition(0).z==8.5f,"Coordinate translation failed");
            receiver.ResetReception();
            Send(Packet("socket",5,"point"));
            Thread.Sleep(50); Call(receiver,"Update");
            Require(receiver.Characters.Length==0,"Old session accepted after reset");
            Console.WriteLine("PASS: UDP completion, coordinates, translation, retired-session rejection");
        } finally { Call(receiver,"OnDisable"); }
        Require(!receiver.IsListening,"Socket shutdown failed");
        using(var rebound=new UdpClient(new IPEndPoint(IPAddress.Any,receiver.listenPort))) {}
        Console.WriteLine("PASS: shutdown releases UDP port");
        using(var busy=new UdpClient(new IPEndPoint(IPAddress.Any,receiver.listenPort))) {
            var blocked=new RemoteInkReceiver { listenPort=receiver.listenPort };
            Call(blocked,"OnEnable");
            Require(!blocked.IsListening && blocked.ReceiveError!=null,"Bind failure must be visible");
            Call(blocked,"OnDisable");
        }
        Console.WriteLine("PASS: occupied UDP port reports bind error");
    }
}
