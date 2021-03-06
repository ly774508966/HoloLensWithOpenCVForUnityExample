﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Threading;

#if UNITY_5_3 || UNITY_5_3_OR_NEWER
using UnityEngine.SceneManagement;
#endif
using OpenCVForUnity;

namespace HoloLensWithOpenCVForUnityExample
{
    /// <summary>
    /// HoloLens ArUco example.
    /// https://github.com/opencv/opencv_contrib/blob/master/modules/aruco/samples/detect_markers.cpp
    /// </summary>
    [RequireComponent(typeof(WebCamTextureToMatHelper))]
    public class HoloLensArUcoExample : MonoBehaviour
    {
        /// <summary>
        /// The enable.
        /// </summary>
        public bool enable = true;

        /// <summary>
        /// The estimate pose.
        /// </summary>
        public bool estimatePose = true;

        /// <summary>
        /// The DOWNSAMPL e_ RATI.
        /// </summary>
        [SerializeField, TooltipAttribute("Factor by which the image will be scaled down before detection.")]
        public float DOWNSCALE_RATIO = 3;

        /// <summary>
        /// The dictionary identifier.
        /// </summary>
        public int dictionaryId = 10;

        /// <summary>
        /// The length of the marker.
        /// </summary>
        public float markerLength = 0.188f;

        /// <summary>
        /// The AR game object.
        /// </summary>
        public GameObject ARGameObject;

        /// <summary>
        /// The AR camera.
        /// </summary>
        public Camera ARCamera;

        /// <summary>
        /// The cam matrix.
        /// </summary>
        Mat camMatrix;

        /// <summary>
        /// The invert Y.
        /// </summary>
        Matrix4x4 invertYM;

        /// <summary>
        /// The transformation m.
        /// </summary>
        Matrix4x4 transformationM;

        /// <summary>
        /// The invert Z.
        /// </summary>
        Matrix4x4 invertZM;

        /// <summary>
        /// The ar m.
        /// </summary>
        Matrix4x4 ARM;

        /// <summary>
        /// The identifiers.
        /// </summary>
        Mat ids ;

        /// <summary>
        /// The corners.
        /// </summary>
        List<Mat> corners;

        /// <summary>
        /// The rejected.
        /// </summary>
        List<Mat> rejected;

        /// <summary>
        /// The rvecs.
        /// </summary>
        Mat rvecs;

        /// <summary>
        /// The tvecs.
        /// </summary>
        Mat tvecs;

        /// <summary>
        /// The rot mat.
        /// </summary>
        Mat rotMat;

        /// <summary>
        /// The detector parameters.
        /// </summary>
        DetectorParameters detectorParams;

        /// <summary>
        /// The dictionary.
        /// </summary>
        Dictionary dictionary;

        /// <summary>
        /// The web cam texture to mat helper.
        /// </summary>
        WebCamTextureToMatHelper webCamTextureToMatHelper;

        /// <summary>
        /// The rgb mat.
        /// </summary>
        Mat rgbMat;

        // Camera matrix value of Hololens camera 896x504 size. 
        // These values ​​are unique to my device, obtained from "Windows.Media.Devices.Core.CameraIntrinsics" class. (https://docs.microsoft.com/en-us/uwp/api/windows.media.devices.core.cameraintrinsics)
        // (can adjust the position of the AR hologram with the values ​​of cx and cy. see http://docs.opencv.org/2.4/modules/calib3d/doc/camera_calibration_and_3d_reconstruction.html)
        private double fx = 1035.149;//focal length x.
        private double fy = 1034.633;//focal length y.
        private double cx = 404.9134;//principal point x.
        private double cy = 236.2834;//principal point y.
        private MatOfDouble distCoeffs;
        private double distCoeffs1 = 0.2036923;//radial distortion coefficient k1.
        private double distCoeffs2 = -0.2035773;//radial distortion coefficient k2.
        private double distCoeffs3 = 0.0;//tangential distortion coefficient p1.
        private double distCoeffs4 = 0.0;//tangential distortion coefficient p2.
        private double distCoeffs5 = -0.2388065;//radial distortion coefficient k3.


        private bool detecting = false;
        private bool ARTransMatrixUpdated = false;
        private readonly static Queue<Action> ExecuteOnMainThread = new Queue<Action>();
        private System.Object sync = new System.Object ();
        private Mat rgbaMat4Thread;
        private Mat downScaleRgbaMat;

        private bool _isThreadRunning = false;
        private bool isThreadRunning {
            get { lock (sync)
                return _isThreadRunning; }
            set { lock (sync)
                _isThreadRunning = value; }
        }

        // Use this for initialization
        void Start ()
        {
            if (DOWNSCALE_RATIO < 1)
                DOWNSCALE_RATIO = 1;

            webCamTextureToMatHelper = gameObject.GetComponent<WebCamTextureToMatHelper> ();
            webCamTextureToMatHelper.Init ();
        }

        /// <summary>
        /// Raises the web cam texture to mat helper inited event.
        /// </summary>
        public void OnWebCamTextureToMatHelperInited ()
        {
            Debug.Log ("OnWebCamTextureToMatHelperInited");

            Mat webCamTextureMat = webCamTextureToMatHelper.GetMat ();

            gameObject.GetComponent<Renderer> ().material.mainTexture = webCamTextureToMatHelper.GetWebCamTexture();

            Debug.Log ("Screen.width " + Screen.width + " Screen.height " + Screen.height + " Screen.orientation " + Screen.orientation);


            float width = webCamTextureMat.width() / DOWNSCALE_RATIO;
            float height = webCamTextureMat.height() / DOWNSCALE_RATIO;
            gameObject.transform.localScale = new Vector3 (1, height/width, 1);

            double fx = this.fx;
            double fy = this.fy;
            double cx = this.cx / DOWNSCALE_RATIO;
            double cy = this.cy / DOWNSCALE_RATIO;

            camMatrix = new Mat (3, 3, CvType.CV_64FC1);
            camMatrix.put (0, 0, fx);
            camMatrix.put (0, 1, 0);
            camMatrix.put (0, 2, cx);
            camMatrix.put (1, 0, 0);
            camMatrix.put (1, 1, fy);
            camMatrix.put (1, 2, cy);
            camMatrix.put (2, 0, 0);
            camMatrix.put (2, 1, 0);
            camMatrix.put (2, 2, 1.0f);
            Debug.Log ("camMatrix " + camMatrix.dump ());

            distCoeffs = new MatOfDouble (distCoeffs1, distCoeffs2, distCoeffs3, distCoeffs4, distCoeffs5);
            Debug.Log ("distCoeffs " + distCoeffs.dump ());

            //Calibration camera
            Size imageSize = new Size (width, height);
            double apertureWidth = 0;
            double apertureHeight = 0;
            double[] fovx = new double[1];
            double[] fovy = new double[1];
            double[] focalLength = new double[1];
            Point principalPoint = new Point (0, 0);
            double[] aspectratio = new double[1];

            Calib3d.calibrationMatrixValues (camMatrix, imageSize, apertureWidth, apertureHeight, fovx, fovy, focalLength, principalPoint, aspectratio);

            Debug.Log ("imageSize " + imageSize.ToString ());
            Debug.Log ("apertureWidth " + apertureWidth);
            Debug.Log ("apertureHeight " + apertureHeight);
            Debug.Log ("fovx " + fovx [0]);
            Debug.Log ("fovy " + fovy [0]);
            Debug.Log ("focalLength " + focalLength [0]);
            Debug.Log ("principalPoint " + principalPoint.ToString ());
            Debug.Log ("aspectratio " + aspectratio [0]);


            rgbMat = new Mat (webCamTextureMat.rows (), webCamTextureMat.cols (), CvType.CV_8UC3);
            ids = new Mat ();
            corners = new List<Mat> ();
            rejected = new List<Mat> ();
            rvecs = new Mat ();
            tvecs = new Mat ();
            rotMat = new Mat (3, 3, CvType.CV_64FC1);


            transformationM = new Matrix4x4 ();

            invertYM = Matrix4x4.TRS (Vector3.zero, Quaternion.identity, new Vector3 (1, -1, 1));
            Debug.Log ("invertYM " + invertYM.ToString ());

            invertZM = Matrix4x4.TRS (Vector3.zero, Quaternion.identity, new Vector3 (1, 1, -1));
            Debug.Log ("invertZM " + invertZM.ToString ());

            detectorParams = DetectorParameters.create ();
            dictionary = Aruco.getPredefinedDictionary (Aruco.DICT_6X6_250);


            //If WebCamera is frontFaceing,flip Mat.
            if (webCamTextureToMatHelper.GetWebCamDevice ().isFrontFacing) {
                webCamTextureToMatHelper.flipHorizontal = true;
            }

            rgbaMat4Thread = new Mat ();
            downScaleRgbaMat = new Mat ();
        }

        /// <summary>
        /// Raises the web cam texture to mat helper disposed event.
        /// </summary>
        public void OnWebCamTextureToMatHelperDisposed ()
        {
            Debug.Log ("OnWebCamTextureToMatHelperDisposed");

            StopThread ();

            if (rgbMat != null)
                rgbMat.Dispose ();
            if (ids != null)
                ids.Dispose ();
            foreach (var item in corners) {
                item.Dispose ();
            }
            corners.Clear ();
            foreach (var item in rejected) {
                item.Dispose ();
            }
            rejected.Clear ();
            if (rvecs != null)
                rvecs.Dispose ();
            if (tvecs != null)
                tvecs.Dispose ();
            if (rotMat != null)
                rotMat.Dispose ();

            if (rgbaMat4Thread != null)
                rgbaMat4Thread.Dispose ();
        }

        /// <summary>
        /// Raises the web cam texture to mat helper error occurred event.
        /// </summary>
        /// <param name="errorCode">Error code.</param>
        public void OnWebCamTextureToMatHelperErrorOccurred(WebCamTextureToMatHelper.ErrorCode errorCode){
            Debug.Log ("OnWebCamTextureToMatHelperErrorOccurred " + errorCode);
        }

        // Update is called once per frame
        void Update ()
        {
            lock (sync) {
                while (ExecuteOnMainThread.Count > 0) {
                    ExecuteOnMainThread.Dequeue ().Invoke ();
                }
            }

            if (webCamTextureToMatHelper.IsPlaying () && webCamTextureToMatHelper.DidUpdateThisFrame ()) {

                if (enable && !detecting ) {
                    detecting = true;

                    rgbaMat4Thread = webCamTextureToMatHelper.GetMat ();

                    StartThread (ThreadWorker);
                }
            }
        }

        private void StartThread(Action action)
        {
            #if UNITY_METRO && NETFX_CORE
            System.Threading.Tasks.Task.Run(() => action());
            #elif UNITY_METRO
            action.BeginInvoke(ar => action.EndInvoke(ar), null);
            #else
            ThreadPool.QueueUserWorkItem (_ => action());
            #endif
        }

        private void StopThread ()
        {
            if (!isThreadRunning)
                return;

            while (isThreadRunning) {
                //Wait threading stop
            } 
        }

        private void ThreadWorker()
        {
            isThreadRunning = true;

            ARUcoDetect ();

            lock (sync) {
                if (ExecuteOnMainThread.Count == 0) {
                    ExecuteOnMainThread.Enqueue (() => {
                        DetectDone ();
                    });
                }
            }

            isThreadRunning = false;
        }

        private void ARUcoDetect()
        {
            if (DOWNSCALE_RATIO == 1) {
                downScaleRgbaMat = rgbaMat4Thread;
            } else {
                Imgproc.resize (rgbaMat4Thread, downScaleRgbaMat, new Size (), 1.0 / DOWNSCALE_RATIO, 1.0 / DOWNSCALE_RATIO, Imgproc.INTER_LINEAR);
            }
            Imgproc.cvtColor (downScaleRgbaMat, rgbMat, Imgproc.COLOR_RGBA2RGB);

            // Detect markers and estimate Pose
            Aruco.detectMarkers (rgbMat, dictionary, corners, ids, detectorParams, rejected);

            if (estimatePose && ids.total () > 0){
                Aruco.estimatePoseSingleMarkers (corners, markerLength, camMatrix, distCoeffs, rvecs, tvecs);

                for (int i = 0; i < ids.total (); i++) {

                    //This example can display ARObject on only first detected marker.
                    if (i == 0) {

                        // Position
                        double[] tvec = tvecs.get (i, 0);

                        // Rotation
                        double[] rv = rvecs.get (i, 0);
                        Mat rvec = new Mat (3, 1, CvType.CV_64FC1);
                        rvec.put (0, 0, rv [0]);
                        rvec.put (1, 0, rv [1]);
                        rvec.put (2, 0, rv [2]);
                        Calib3d.Rodrigues (rvec, rotMat);

                        transformationM.SetRow (0, new Vector4 ((float)rotMat.get (0, 0) [0], (float)rotMat.get (0, 1) [0], (float)rotMat.get (0, 2) [0], (float)tvec [0]));
                        transformationM.SetRow (1, new Vector4 ((float)rotMat.get (1, 0) [0], (float)rotMat.get (1, 1) [0], (float)rotMat.get (1, 2) [0], (float)tvec [1]));
                        transformationM.SetRow (2, new Vector4 ((float)rotMat.get (2, 0) [0], (float)rotMat.get (2, 1) [0], (float)rotMat.get (2, 2) [0], (float)(tvec [2] / DOWNSCALE_RATIO)));

                        transformationM.SetRow (3, new Vector4 (0, 0, 0, 1));

                        // Right-handed coordinates system (OpenCV) to left-handed one (Unity)
                        ARM = invertYM * transformationM;

                        // Apply Z axis inverted matrix.
                        ARM = ARM * invertZM;

                        ARTransMatrixUpdated = true;

                        break;
                    }
                }
            }

            /*
            // Draw results
            if (ids.total () > 0) {
                Aruco.drawDetectedMarkers (rgbMat, corners, ids, new Scalar (255, 0, 0));

                if (estimatePose) {
                    for (int i = 0; i < ids.total (); i++) {
                        Aruco.drawAxis (rgbMat, camMatrix, distCoeffs, rvecs, tvecs, markerLength * 0.5f);
                    }
                }
            }
            */
        }

        private void DetectDone()
        {
            if (estimatePose) {
                if (ARTransMatrixUpdated) {
                    ARTransMatrixUpdated = false;

                    // Apply camera transform matrix.
                    ARM = ARCamera.transform.localToWorldMatrix * ARM;
                    ARUtils.SetTransformFromMatrix (ARGameObject.transform, ref ARM);
                }
            }

            detecting = false;
        }

        /// <summary>
        /// Raises the disable event.
        /// </summary>
        void OnDisable ()
        {
            webCamTextureToMatHelper.Dispose ();
        }

        /// <summary>
        /// Raises the back button event.
        /// </summary>
        public void OnBackButton ()
        {
            #if UNITY_5_3 || UNITY_5_3_OR_NEWER
            SceneManager.LoadScene ("HoloLensWithOpenCVForUnityExample");
            #else
            Application.LoadLevel ("HoloLensWithOpenCVForUnityExample");
            #endif
        }

        /// <summary>
        /// Raises the play button event.
        /// </summary>
        public void OnPlayButton ()
        {
            webCamTextureToMatHelper.Play ();
        }

        /// <summary>
        /// Raises the pause button event.
        /// </summary>
        public void OnPauseButton ()
        {
            webCamTextureToMatHelper.Pause ();
        }

        /// <summary>
        /// Raises the stop button event.
        /// </summary>
        public void OnStopButton ()
        {
            webCamTextureToMatHelper.Stop ();
        }

        /// <summary>
        /// Raises the change camera button event.
        /// </summary>
        public void OnChangeCameraButton ()
        {
            webCamTextureToMatHelper.Init (null, webCamTextureToMatHelper.requestWidth, webCamTextureToMatHelper.requestHeight, !webCamTextureToMatHelper.requestIsFrontFacing);
        }
    }
}