﻿using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using VRC.OSCQuery.Samples.Shared;

#pragma warning disable 4014

namespace VRC.OSCQuery.Samples.Tracking
{
    public class TrackingCanvas : MonoBehaviour
    {
        // Scene Objects
        public Text HeaderText;
        
        // Connects to default OSC endpoint instead of searching via OSCQuery
        public bool connectToDefaultVRCEndpoint;

        // Service
        private OSCQueryService _oscQueryService;
        // List of receivers to send to
        private List<OscClientPlus> _receivers = new List<OscClientPlus>();
        private string _serverName = "TrackingServer";
        private const int RefreshServicesInterval = 10;

        // Constant strings
        private const string TRACKERS_ROOT = "/tracking/trackers";
        private const string TRACKERS_POSITION = "position";
        private const string TRACKERS_ROTATION = "rotation";

        public float userHeight = 1.7f; // Default height, change for your real-world height in meters
        private float _avatarHeight = 1.89f; // Measured by hand for now, the world-space position of the top of the Avatar's head

        public Transform headTransform;
        public List<Transform> trackerTransforms;

        void Start()
        {
            // Starts an OSCQuery Server, listens for Chatbox Services
            StartService();
            
            // Connects to default local VRC client for direct testing
            if (connectToDefaultVRCEndpoint)
            {
                AddTrackingReceiver(IPAddress.Loopback, 9000);
            }
            
            // Check for new Services regularly
            InvokeRepeating(nameof(RefreshServices), 1, RefreshServicesInterval);
        }
        
        private void RefreshServices()
        {
            _oscQueryService.RefreshServices();
        }
        
        // Creates a new OSCClient for each new Tracking-capable receiver found
        private async void OnOscQueryServiceFound(OSCQueryServiceProfile profile)
        {
            await UniTask.SwitchToMainThread();
            
            if (await ServiceSupportsTracking(profile))
            {
                var hostInfo = await OSCQuery.Extensions.GetHostInfo(profile.address, profile.port);
                HeaderText.text =
                    $"Sending to {profile.name} at {profile.address}:{hostInfo.oscPort}";
                AddTrackingReceiver(profile.address, hostInfo.oscPort);
            }
            else
            {
                Debug.Log($"Could not find required endpoint on {profile.name}");
            }
        }
        
        // Does the actual construction of the OSC Client, and advertises this service
        private void AddTrackingReceiver(IPAddress address, int port)
        {
            var receiver = new OscClientPlus(address.ToString(), port);
            _receivers.Add(receiver);
            _oscQueryService.AdvertiseOSCService(_serverName, port);
        }
        
        // Checks for compatibility by looking for matching Chatbox root node
        private async Task<bool> ServiceSupportsTracking(OSCQueryServiceProfile profile)
        {
            var tree = await OSCQuery.Extensions.GetOSCTree(profile.address, profile.port);
            return tree.GetNodeWithPath(TRACKERS_ROOT) != null;
        }
        
        private void StartService()
        {
            // Create a new OSCQueryService, advertise
            var port = VRC.OSCQuery.Extensions.GetAvailableTcpPort();
            _oscQueryService = new OSCQueryService(_serverName,  port, 0, new UnityMSLogger());
            Debug.Log($"Starting OSCQueryService {_serverName} on {port}");
            
            // Listen for other services
            _oscQueryService.OnOscQueryServiceAdded += OnOscQueryServiceFound;

            var services = _oscQueryService.GetOSCQueryServices();
            
            // Trigger event for any existing OSCQueryServices
            foreach (var profile in services)
            {
                OnOscQueryServiceFound(profile);
            }
            
            // Query network for services
            _oscQueryService.RefreshServices();
        }

        // Send Tracker updates if ready
        void Update()
        {
            // Exit early if we don't have the required Transforms
            if (_receivers.Count > 0)
            {
                foreach (var receiver in _receivers)
                {
                    SendTrackerDataToReceiver("head", headTransform, receiver);
                    if (trackerTransforms != null)
                    {
                        for (int i = 0; i < trackerTransforms.Count; i++)
                        {
                            SendTrackerDataToReceiver((i+1).ToString(), trackerTransforms[i], receiver);
                        }
                    }
                }
            }
        }

        /// Convenience function to send tracker data from a transform and name
        /// The Head data should be sent like this:
        /// `/tracking/trackers/head/position`
        /// `/tracking/trackers/head/rotation`
        ///
        /// The Tracker data should be sent like this, where `i` is the number of the tracker between 1-8 (no tracker 0!)
        /// `/tracking/trackers/i/position`
        /// `/tracking/trackers/i/rotation`
        
        private void SendTrackerDataToReceiver(string trackerName, Transform target, OscClientPlus receiver)
        {
            // Exit early if transform is null
            if (!target) return;
            
            var newPosition = ScaleToUserHeight(target.position);
            receiver.Send($"{TRACKERS_ROOT}/{trackerName}/{TRACKERS_POSITION}", newPosition);
            receiver.Send($"{TRACKERS_ROOT}/{trackerName}/{TRACKERS_ROTATION}", target.rotation.eulerAngles);   
        }

        // Required in order to scale between world and user differences
        private Vector3 ScaleToUserHeight(Vector3 targetPosition)
        {
            return targetPosition * (userHeight / _avatarHeight);
        }

        private void OnDestroy()
        {
            _oscQueryService.Dispose();
        }
    }

}