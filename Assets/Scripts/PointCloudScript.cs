using Microsoft.Azure.Kinect.Sensor;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.VFX;

public class PointCloudScript : MonoBehaviour
{
    //Variable for handling Kinect
    Device kinect;
    //Number of all points of PointCloud 
    int num;
    //Used to draw a set of points
    Mesh mesh;
    //Array of coordinates for each point in PointCloud
    Vector3[] vertices;
    //Array of colors corresponding to each point in PointCloud
    Color32[] colors;
    //List of indexes of points to be rendered
    int[] indices;
    //Class for coordinate transformation(e.g.Color-to-depth, depth-to-xyz, etc.)
    Transformation transformation;

    [SerializeField]
    UnityEngine.UI.Image debugRenderDepth;
    [SerializeField]
    UnityEngine.UI.Image debugAfterCompute;

    public float dataScale = 10f;
    public float threshold = 10f;
    public float stepx = 10f;
    public float stepy = 10f;

    Sprite debugRenderDepthSprite;
    Sprite debugAfterComputeSprite;

    UnityEngine.Texture2D depthTex;
    UnityEngine.Texture2D debugAfterComputeTex;
    UnityEngine.Texture3D forceField;

    public VisualEffect vfx;

    void Start()
    {
        //The method to initialize Kinect
        InitKinect();
        //Initialization for point cloud rendering
        InitMesh();

        Debug.Log($"Creating texture of size {depthTex.width}x{depthTex.height}={depthTex.width*depthTex.height}px for {num} pixels");

        //Loop to get data from Kinect and rendering
        Task t = KinectLoop();
    }

    //Initialization of Kinect
    private void InitKinect()
    {
        //Connect with the 0th Kinect
        kinect = Device.Open(0);
        //Setting the Kinect operation mode and starting it
        kinect.StartCameras(new DeviceConfiguration
        {
            ColorFormat = ImageFormat.ColorBGRA32,
            ColorResolution = ColorResolution.R720p,
            DepthMode = DepthMode.NFOV_Unbinned,
            SynchronizedImagesOnly = true,
            CameraFPS = FPS.FPS30
        });
        //Access to coordinate transformation information
        transformation = kinect.GetCalibration().CreateTransformation();
        depthTex = new Texture2D(kinect.GetCalibration().DepthCameraCalibration.ResolutionWidth,
            kinect.GetCalibration().DepthCameraCalibration.ResolutionHeight, TextureFormat.R16, false);
        debugAfterComputeTex = new Texture2D(kinect.GetCalibration().DepthCameraCalibration.ResolutionWidth,
            kinect.GetCalibration().DepthCameraCalibration.ResolutionHeight, TextureFormat.RGB24, false);

        forceField = new Texture3D(kinect.GetCalibration().DepthCameraCalibration.ResolutionWidth,
            kinect.GetCalibration().DepthCameraCalibration.ResolutionHeight, 1, TextureFormat.RGB24, false);

        Rect bounds = new Rect(0, 0, depthTex.width, depthTex.height);
        Vector2 pivot = bounds.center;

        debugRenderDepthSprite = Sprite.Create(depthTex, bounds, pivot);
        debugAfterComputeSprite = Sprite.Create(debugAfterComputeTex, bounds, pivot);

        debugAfterCompute.sprite = debugAfterComputeSprite;
        debugRenderDepth.sprite = debugRenderDepthSprite;

    }

    //Prepare to draw point cloud.
    private void InitMesh()
    {
        //Get the width and height of the Depth image and calculate the number of all points
        int width = kinect.GetCalibration().DepthCameraCalibration.ResolutionWidth;
        int height = kinect.GetCalibration().DepthCameraCalibration.ResolutionHeight;
        num = width * height;

        //Instantiate mesh
        mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        //Allocation of vertex and color storage space for the total number of pixels in the depth image
        vertices = new Vector3[num];
        colors = new Color32[num];
        indices = new int[num];

        //Initialization of index list
        for (int i = 0; i < num; i++)
        {
            indices[i] = i;
        }


        //Allocate a list of point coordinates, colors, and points to be drawn to mesh
        mesh.vertices = vertices;
        mesh.colors32 = colors;
        mesh.SetIndices(indices, MeshTopology.Points, 0);

        gameObject.GetComponent<MeshFilter>().mesh = mesh;
    }

    public ComputeShader shader;
    public void ApplyComputeShader()
    {
        int kernelHandle = shader.FindKernel("CSMain");
        RenderTexture tex = new RenderTexture(depthTex.width, depthTex.height, 16);
        tex.enableRandomWrite = true;
        tex.Create();

        shader.SetTexture(kernelHandle, "Result", tex);
        shader.SetTexture(kernelHandle, "ImageInput", depthTex);
        shader.SetFloat("dataScale", dataScale);
        shader.SetFloat("threshold", threshold);
        shader.SetFloat("width", depthTex.width);
        shader.SetFloat("height", depthTex.height);
        shader.SetFloat("stepx", stepx);
        shader.SetFloat("stepy", stepy);
        shader.Dispatch(kernelHandle, depthTex.width / 8, depthTex.height / 8, 1);

        RenderTexture.active = tex;
        debugAfterComputeTex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
        debugAfterComputeTex.Apply();

        RenderTexture.active = tex;
        forceField.SetPixelData<float>(debugAfterComputeTex.GetPixelData<float>(0), 0);
        forceField.Apply();
        ParticleSystemForceField ff = GetComponent<ParticleSystemForceField>();
        vfx.SetTexture("Texture",debugAfterComputeTex);
    }
    public UnityEngine.Texture2D KinectDepthToTexture2D(Image img)
    {
        ushort[] xyzArray = img.GetPixels<ushort>().ToArray();
        depthTex.SetPixelData<ushort>(xyzArray, 0);
        depthTex.Apply();

        NativeArray<ushort> s = depthTex.GetRawTextureData<ushort>();
        //s.CopyFrom(xyzArray);

        Color c1 = Color.white;
        Color c2 = Color.black;

        /*for (int i = 0; i < num; i++)
        {
            s[i] =  (ushort)(100 * s[i]);
        }
        depthTex.Apply();*/

        ApplyComputeShader();
       

        return depthTex;
    }

    private async Task KinectLoop()
    {
        while (true)
        {
            using (Capture capture = await Task.Run(() => kinect.GetCapture()).ConfigureAwait(true))
            {
                //Getting color information
                Image colorImage = transformation.ColorImageToDepthCamera(capture);
                BGRA[] colorArray = colorImage.GetPixels<BGRA>().ToArray();

                //Getting vertices of point cloud
                Image xyzImage = transformation.DepthImageToPointCloud(capture.Depth);
                KinectDepthToTexture2D(capture.Depth);
                Short3[] xyzArray = xyzImage.GetPixels<Short3>().ToArray();

                /*for (int i = 0; i < num; i++)
                {
                    vertices[i].x = xyzArray[i].X * 0.001f;
                    vertices[i].y = -xyzArray[i].Y * 0.001f;//上下反転
                    vertices[i].z = xyzArray[i].Z * 0.001f;

                    colors[i].b = colorArray[i].B;
                    colors[i].g = colorArray[i].G;
                    colors[i].r = colorArray[i].R;
                    colors[i].a = 255;
                }*/

                /*mesh.vertices = vertices;
                mesh.colors32 = colors;
                mesh.RecalculateBounds();*/
            }
        }
    }

    //Stop Kinect as soon as this object disappear
    private void OnDestroy()
    {
        kinect.StopCameras();
    }

}
