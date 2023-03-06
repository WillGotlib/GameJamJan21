using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.Collections;
using protoBuff;
using RSG;
using Unity.WebRTC;
using UnityEngine;

namespace Online
{
    public delegate void OnMessageCallback(Response action);

    internal class WebRtc
    {
        //todo group events
        public void SendPriority(Request request)
        {
            if (!Alive) return;

            if (!_priorityOpen)
            {
                _priorityIdleQueue.Enqueue(request);
                return;
            }
            
            try
            {
                _priorityChannel.Send(request.ToByteArray());
            }
            catch (Exception e)
            {
                Debug.Log(e);
                Alive = false;
            }
        }
        public void SendFast(Request request)
        {
            if (!Alive) return;
            if (!_fastOpen)
            {
                _fastIdleQueue.Enqueue(request);
                return;
            }
            
            try
            {
                _fastChannel.Send(request.ToByteArray());
            }
            catch (Exception e)
            {
                Debug.Log(e);
                Alive = false;
            }
        }

        private WS_WRTC _wsWrtc;
        
        private RTCPeerConnection _connection;
        private RTCDataChannel _priorityChannel;
        private RTCDataChannel _fastChannel;
        private bool _priorityOpen;
        private bool _fastOpen;
        private readonly Queue<Request> _priorityIdleQueue;
        private readonly Queue<Request> _fastIdleQueue;

        public bool Alive { private set; get; }
        
        public OnMessageCallback callback = Debug.Log; // just log the responses by default

        public WebRtc()
        {
            _priorityIdleQueue = new Queue<Request>();
            _fastIdleQueue = new Queue<Request>();

            var config = new RTCConfiguration
            {
                iceServers = new[] { new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } } }
            };
            _connection = new RTCPeerConnection(ref config);
            _wsWrtc = new WS_WRTC(_connection);
            
            var priorityConf = new RTCDataChannelInit();
            var fastConf = new RTCDataChannelInit
            {
                // maxPacketLifeTime = 0,
                maxRetransmits = 0,
                ordered = false,
            };
            _priorityChannel = CreateDataChannel(_connection, "priority", priorityConf, () =>
            {
                _priorityOpen = true;
                SendPriority(GroupQueue(_priorityIdleQueue));
            });
            _fastChannel = CreateDataChannel(_connection, "fast", fastConf, () =>
            {
                _fastOpen = true;
                SendFast(GroupQueue(_fastIdleQueue));
            });
        }
        
        public Promise Connect(MonoBehaviour parent, string token)
        {
            var promise = new Promise();
            _wsWrtc.Connect(parent, token).Then(() =>
            {
                Alive = true;
                promise.Resolve();
            }).Catch(promise.Reject);
            return promise;
        }

        public void Disconnect()
        {
            _connection.Close();
            _fastOpen = false;
            _priorityOpen = false;
            Alive = false;
        }

        private RTCDataChannel CreateDataChannel(RTCPeerConnection pc, string name, RTCDataChannelInit config, DelegateOnOpen onOpen)
        {
            var dataChannel = pc.CreateDataChannel(name, config);
            dataChannel.OnOpen = onOpen;
            dataChannel.OnMessage = data =>
            {
                var message = Response.Parser.ParseFrom(data);
                callback(message);
            };
            return dataChannel;
        }

        private static Request GroupQueue(Queue<Request> queue)
        {
            var newReq = new RepeatedField<StreamAction>();
            while (queue.Count>0)
                foreach (var streamAction in queue.Dequeue().Requests)
                    newReq.Add(streamAction);

            return new Request {Requests = { newReq }};
        }

    }
}