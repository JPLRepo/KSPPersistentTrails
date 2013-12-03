﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PersistentTrails
{
    //[KSPAddon(KSPAddon.Startup.Flight, false)]
    //public class RenderHook : MonoBehaviour
    //{
    //    public override void OnPostRender() {
        
    //    }
    //}

    //This is the central instance of this plugin. It organizes resources and settings and creates the TrackManager
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class ExplorerTrackBehaviour :  MonoBehaviour
    {
        //private TrackManager trackManager;
        private Vector3 lastReferencePos;

        MainWindow mainWindow;
        bool ShouldBeInPostDrawQueue = false;
        bool IsInPostDrawQueue = false;

        private float recordingInterval;
        public float RecordingInterval { get { return recordingInterval; } set { recordingInterval = value; setupRepeatingUpdate(recordingInterval); } }


        //Awake Event - when the DLL is loaded
        public ExplorerTrackBehaviour()
        {
            mainWindow = new MainWindow(this);
            recordingInterval = 5;
        }

        ~ExplorerTrackBehaviour()
        {
            Debug.Log("ExplorerTrackBehaviour destructor");
            //trackManager = null;
            mainWindow = null;

        }

        //Called once everything else is loaded, just before the first execution tick 
        public void Awake() {
            //DontDestroyOnLoad(this);
            Debug.Log("Awakening ExplorerTrackBehaviour");

            GUIResources.LoadAssets();
            
            //mainWindowOpen = false;
     
            //Load config
            //KSP.IO.PluginConfiguration configfile = KSP.IO.PluginConfiguration.CreateForType<ExplorerTrackBehaviour>();
            //configfile.load();

            //ManifestPosition = PrevManifestPosition = configfile.GetValue<Rect>("ManifestPosition");
            //TransferPosition = PrevTransferPosition = configfile.GetValue<Rect>("TransferPosition");
            //RosterPosition = PrevRosterPosition = configfile.GetValue<Rect>("RosterPosition");
            //ButtonPosition = PrevButtonPosition = configfile.GetValue<Rect>("ButtonPosition");

            //trackManager.restoreTracksFromFile();
            setupRepeatingUpdate(recordingInterval);
            lastReferencePos = new Vector3(0,0,0);
            
            
            InvokeRepeating("checkGPS", 1, 1); //Cancel with CancelInvoke
            

        }

        public void OnGUI() {

            //Do the GUI Stuff - basically get the workers draw stuff into the postrendering queue
            //If the two flags are different are we going in or out of the queue
            if (ShouldBeInPostDrawQueue != IsInPostDrawQueue)
            {
                if (ShouldBeInPostDrawQueue /* && !IsInPostDrawQueue*/)
                {
                    //Add to the queue
                    RenderingManager.AddToPostDrawQueue(5, DrawGUI);
                    IsInPostDrawQueue = true;
                }
                else
                {
                    RenderingManager.RemoveFromPostDrawQueue(5, DrawGUI);
                    IsInPostDrawQueue = false;
                }
            }

            TrackManager.Instance.updateAllLabelPositions();


        }

        public void setupRepeatingUpdate(float updateIntervalSeconds)
        {
            CancelInvoke("updateCurrentTrack");
            InvokeRepeating("updateCurrentTrack", updateIntervalSeconds, updateIntervalSeconds); //Cancel with CancelInvoke
            //StartCoroutine(coroutineUpdate());
        }

        //Called via MonoBehavious.InvokeRepeating
        public void updateCurrentTrack()
        {
            TrackManager.Instance.updateCurrentTrack();
        }

        public void checkGPS()
        {
            TrackManager.Instance.checkGPS();
        }

        public void Update() { 
            //check if floating origin has updated by checking reference position in Unity coords
            //Debug.Log("TrackManager Behaviour Update");
            Vector3 newReferencePos = FlightGlobals.currentMainBody.GetWorldSurfacePosition(0, 0, 1000);
            if ((newReferencePos - lastReferencePos).sqrMagnitude > 1.0f)
            {
                TrackManager.Instance.OnFloatingOriginUpdated();
                lastReferencePos = newReferencePos;
            }

            if (HighLogic.LoadedScene == GameScenes.FLIGHT)
            {
                ShouldBeInPostDrawQueue = true;
            }
            else
            {
                ShouldBeInPostDrawQueue = false;
            }

            //if ((LastGameUT != 0) && (LastGameUT > Planetarium.GetUniversalTime()))
            //{
            //    KACWorker.DebugLogFormatted("Time Went Backwards - Load or restart - resetting inqueue flag");
            //    ShouldBeInPostDrawQueue = false;
            //}
            //TireRecorder.Instance.update();

            // --- TEST CRAFT SERIALIZE ---
            //if (Input.GetKeyDown(KeyCode.F8))
            //    CraftLoader.saveCraftToFile();
        }

        public void DrawGUI()
        {
            GUIResources.SetupGUI();
            //Debug.Log("DrawMainButton");
            

            if (HighLogic.LoadedScene == GameScenes.FLIGHT)
            {
                var icon = TrackManager.Instance.IsRecording ? GUIResources.IconRecording : GUIResources.IconNoRecording;

                //Debug.Log("loaded button icon");
                //Debug.Log("Rect: " + Resources.mainButtonPosition.ToString());
                //Debug.Log("IconStyle: " + Resources.IconStyle.ToString());
                if (GUI.Button(GUIResources.mainButtonPosition, new GUIContent(icon, "ExplorerTracks MainWindow"), GUIResources.IconStyle))
                {

                    //Debug.Log("toggling mainGui");
                    mainWindow.ToggleVisible();
                }


            }

            
        }


    }

    class TrackManager {
        public List<Track> allTracks;
        private Track activeTrack;
        private bool recording;

        private bool allowRecording; //Disabled if Figaro-GPS-Receiver is onboard, but not enough transmitters are available
        public bool isRecordingAllowed() { return allowRecording; }

        //private ExplorerTrackBehaviour behaviour;
        public RecordingThresholds ChangeThresholds{get; set;}


        public bool IsRecording { get { return recording; } }

        //Trackmanager is a singleton
        public static readonly TrackManager Instance = new TrackManager();

        private TrackManager()
        {
            activeTrack = null;
            recording = false;

            allTracks = new List<Track>();
            //this.behaviour = behaviour;

            GameEvents.onVesselDestroy.Add(delegate(Vessel v)
            {
                if (activeTrack != null && activeTrack.SourceVessel == v)
                    stopRecording();
            });
            GameEvents.onDominantBodyChange.Add(delegate(GameEvents.FromToAction<CelestialBody, CelestialBody> fromToAction) {
                stopRecording();
            });
            GameEvents.onFlightReady.Add(delegate() {
                restoreTracksFromFile();
            });

            GameEvents.onGameSceneLoadRequested.Add(delegate(GameScenes scene)
            {
                clearTracks();
            });
            
        }

        ~TrackManager() {
            clearTracks();
        }

        private void clearTracks() {
            stopRecording();

            Debug.Log("Trackmanager - cleaning up");
            foreach (Track t in allTracks)
            {
                t.Visible = false;
            }

            allTracks.Clear();
        }

        public void stopRecording()
        {
            activeTrack = null;
            recording = false;
        }
        public void startNewTrack()
        {
            Debug.Log("Starting new Track");
            recording = true;

            
            //create new Track
            activeTrack = new Track(); //initializes with TrackName = activeVessel.Name

            CraftLoader.saveCraftToFile();

            activeTrack.TrackName = Utilities.makeUniqueTrackName(activeTrack.TrackName, ref allTracks, false);
            allTracks.Add(activeTrack);
            updateCurrentTrack();

        }

        public void continueTrack(Track track)
        {
            Debug.Log("TrackManager continueTrack()");
            stopRecording();

            CraftLoader.saveCraftToFile();

            recording = true;
            track.SourceVessel = FlightGlobals.ActiveVessel;
            track.VesselName = track.SourceVessel.name;

            activeTrack = track;
            updateCurrentTrack();
        }
        public void deleteTrack(ref Track track)
        {
            allTracks.Remove(track);

            track.Visible = false;
            Debug.Log("removing track");
            if (File.Exists(Utilities.TrackPath + track.TrackName + ".trk"))
            {
                Debug.Log("deleting track-file");
                File.Delete(Utilities.TrackPath + track.TrackName + ".trk");
            }
                

            if (track == activeTrack) {
                stopRecording();
                activeTrack = null;
            }
            track = null;
            
        }

        public void AddLogEntry(String label, String description)
        {
            if (activeTrack != null)
                activeTrack.addLogEntry(label, description);
        }

        //Called via MonoBehavious.InvokeRepeating
        public void updateCurrentTrack()
        {
            //Debug.Log("TrackManager updateCurrentTrack()");
            if (recording)
            {
                if (allowRecording)
                    activeTrack.tryAddWaypoint(ChangeThresholds);
                else
                    stopRecording();
            }
        }

        public void updateAllLabelPositions()
        {
            foreach (Track t in allTracks)
            {
                t.updateNodeLabelPositions();
            }
        }


        // ------------------- FILE IO -----------
        public void saveTracks() {
            Debug.Log("saving tracks");
            foreach (Track track in allTracks) {
                if (track.Modified) {
                    Debug.Log("Found modified track");
                    //string timestring = DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss");
                    //Debug.Log("timestring created: " + timestring);

                    StreamWriter writer = new StreamWriter(Utilities.TrackPath + track.TrackName + ".trk");
                    //Debug.Log("serializing track");
                    writer.WriteLine(track.serialized());
                    writer.Close();
                    track.Modified = false;
                    Debug.Log("wrote track to " + Utilities.TrackPath + track.TrackName + ".trk");
                }
            }
        }

        public void restoreTracksFromFile()
        {
            clearTracks();


            string[] files = Directory.GetFiles(Utilities.TrackPath, "*.trk");

            foreach (string trackFile in files)
            {
                allTracks.Add(new Track(trackFile));
            }
            Debug.Log("restored " + allTracks.Count + " tracks from files");
            
        }

        public void OnFloatingOriginUpdated()
        {
            //Debug.Log("Floating Origin update detected, recalculating Tracks");
            foreach (Track t in allTracks) {
                t.setupRenderer();
            }
        }

        public bool checkGPS()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            //check if sourceVessel has a gps receiver partModule
            Part receiverPart = vessel.Parts.Find(t => t.name == "FigaroReceiver");

            if (!receiverPart){

                //Debug.Log("Found no GPSReceiver Part-aborting");
                allowRecording = true; //Players not using FigaroGPS are unaffected
                return true;
            }

            if(receiverPart.Modules.Contains("KerbalGPS")) {
                PartModule receiverModule = receiverPart.Modules["KerbalGPS"];

                //Debug.Log("Found KerbalGPS Module in ReceiverPart");
                BaseField numSatField = receiverModule.Fields["guNumSats"];

                //Debug.Log("Found num sats field: guiName=" + numSatField.guiName);
                //Debug.Log("checking value(host=receiverModule)=" + numSatField.GetValue(receiverModule));
                int numSats = int.Parse(numSatField.GetValue(receiverModule).ToString());
                if (numSats >= 4)
                {
                    allowRecording = true;
                    return true;
                }
            }

            allowRecording = false;
            return false;
        }//
    }
}
