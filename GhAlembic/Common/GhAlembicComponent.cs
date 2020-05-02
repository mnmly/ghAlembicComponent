using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using WebSocketSharp;
using Newtonsoft.Json;

namespace MNML


{
    public class GhAlembicComponent : GH_Component
    {

        [DllImport("ghAlembic")] static extern IntPtr AbcWriterCreateInstance();
        [DllImport("ghAlembic")] static extern bool AbcWriterOpen(IntPtr instance, String filepath);
        [DllImport("ghAlembic")] static extern void AbcWriterClose(IntPtr instance);
        [DllImport("ghAlembic")] static extern void AbcWriterAddPolyMesh(IntPtr instance, String name,
            String materialName,
            float[] vertices, int numVertices,
            float[] normals, int numNormals,
            float[] uvs, int numUVs,
            int[] faces, int numFaces, int numFaceCount, bool _flipAxis);

        [DllImport("ghAlembic")] static extern void AbcWriterAddCurve(IntPtr instance, String name,
            float[] vertices, int numVertices, 
            int degree, bool periodic, bool _flipAxis);

        [DllImport("ghAlembic")]
        static extern void AbcWriterAddCurveEx(IntPtr instance, String name,
            float[] vertices, int totalVertices,
            int[] numVertices, int numCurves,
            bool periodic,
            float[] widths,
            float[] uvs,
            float[] normals,
            float[] weights,
            int[] orders, float[] knots, bool _flipAxis);

        IntPtr instance;
        WebSocket socket = null;

        Debouncer debouncer = new Debouncer(TimeSpan.FromMilliseconds(200));

        /// <summary>
        /// Each implementation of GH_Component must provide a public 
        /// constructor without any arguments.
        /// Category represents the Tab in which the component will appear, 
        /// Subcategory the panel. If you use non-existing tab or panel names, 
        /// new tabs/panels will automatically be created.
        /// </summary>
        public GhAlembicComponent()
          : base("Export as Alembic", "Export Alembic",
            "Export mesh as Alembic",
            "MNML", "Export")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // Use the pManager object to register your input parameters.
            // You can often supply default values when creating parameters.
            // All parameters must have the correct access type. If you want 
            // to import lists or trees of values, modify the ParamAccess flag.
            pManager.AddGeometryParameter("Geometry", "G", "Geometry (Curve or Mesh)", GH_ParamAccess.list);
            pManager.AddTextParameter("Object Names", "ON", "Object Names", GH_ParamAccess.list);
            pManager.AddTextParameter("Material Names", "MN", "Material Names", GH_ParamAccess.list);
            pManager.AddTextParameter("Output Path", "P", "Output Path", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Flip Axis", "F", "Map Rhino Z to OBJ Y", GH_ParamAccess.item, true);
            pManager.AddGenericParameter("Socket", "S", "Established Websocket Client", GH_ParamAccess.item);
            pManager.AddTextParameter("Collection Name", "C", "Name of Collection", GH_ParamAccess.item, "Collection-Grasshopper");

            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;

        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            // Use the pManager object to register your output parameters.
            // Output parameters do not have default values, but they too must have the correct access type.

            // Sometimes you want to hide a specific parameter from the Rhino preview.
            // You can use the HideParameter() method as a quick way:
            //pManager.HideParameter(0);
        }

        Tuple<List<float>, List<int>, List<float>, List<float>, List<int>> ProcessCurve(Curve _curve, string name)
        {

            var points = new List<float>();
            var numVert = new List<int>();
            var weights = new List<float>();
            var knots = new List<float>();
            var degrees = new List<int>();
            if (_curve is PolyCurve)
            {
                var polycurve = (_curve as PolyCurve);
                for (var i = 0; i < polycurve.SegmentCount; i++)
                {
                    var curve = polycurve.SegmentCurve(i);
                    var result = ProcessCurve(curve, name);
                    points.AddRange(result.Item1);
                    numVert.AddRange(result.Item2);
                    weights.AddRange(result.Item3);
                    knots.AddRange(result.Item4);
                    degrees.AddRange(result.Item5);
                }
                return new Tuple<List<float>, List<int>, List<float>, List<float>, List<int>>(
                    points,
                    numVert,
                    weights,
                    knots,
                    degrees
                );
            }
            else if (_curve is LineCurve) {
                var curve = _curve as LineCurve;
                var pointCount = 2;
                numVert.Add(pointCount);
                for (var i = 0; i<pointCount + _curve.Degree - 1; i++) {
                    knots.Add(0); // is it okay...?
                }
                for (var i = 0; i<pointCount; i++) {
                    weights.Add(1);
                }
                degrees.Add((int)curve.Degree);
                points.Add((float)curve.PointAtStart.X);
                points.Add((float)curve.PointAtStart.Y);
                points.Add((float)curve.PointAtStart.Z);
                points.Add((float)curve.PointAtEnd.X);
                points.Add((float)curve.PointAtEnd.Y);
                points.Add((float)curve.PointAtEnd.Z);
            }
            else if (_curve is NurbsCurve)
            {
                var curve = (_curve as NurbsCurve);
                var ctPoints = curve.Points;
                for (var i = 0; i < ctPoints.Count; i++)
                {
                    weights.Add((float)ctPoints[0].Weight);
                    points.Add((float)ctPoints[i].X);
                    points.Add((float)ctPoints[i].Y);
                    points.Add((float)ctPoints[i].Z);
                }
                numVert.Add(ctPoints.Count);
                var _knots = new List<double>(curve.Knots);
                knots = _knots.Select(i => (float)i).ToList();
                degrees.Add((int)curve.Degree);
            }
            else if (_curve is PolylineCurve)
            {
                // Better not to have it convert.
                var curve = (_curve as PolylineCurve).ToNurbsCurve();
                return ProcessCurve(curve, name);
            }
            else if (_curve is ArcCurve)
            {
                var c = _curve.ToNurbsCurve();
                _curve = c.Rebuild(c.Points.Count, c.Degree + 1, true);
                return ProcessCurve(_curve, name);
            }

            return new Tuple<List<float>, List<int>, List<float>, List<float>, List<int>>(
                points,
                numVert,
                weights,
                knots,
                degrees
            );
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // First, we need to retrieve all data from the input parameters.
            // We'll start by declaring variables and assigning them starting values.
            var meshes = new List<GeometryBase>();
            var objectNames = new List<String>();
            var materialNames = new List<String>();
            var collectionName = "";
            var flip = true;

            String path = "";


            if (!DA.GetDataList(0, meshes)) return;
            if (!DA.GetData(3, ref path)) return;
            if (!DA.GetData(5, ref socket)) { socket = null; }
            DA.GetDataList(1, objectNames);
            DA.GetDataList(2, materialNames);
            DA.GetData(4, ref flip);
            DA.GetData(6, ref collectionName);

            Action action = () =>
            {

                instance = AbcWriterCreateInstance();
                AbcWriterOpen(instance, path);

                var names = new List<String>();

                for (int j = 0; j < meshes.Count; j++)
                {
                    var name = objectNames.Count > j ? objectNames[j] : ("object-" + j);
                    names.Add(name);

                    if (meshes[j] is Mesh)
                    {
                        var mesh = meshes[j] as Mesh;
                        var materialName = materialNames.Count > j ? materialNames[j] : "Default";

                        mesh.Normals.ComputeNormals();

                        var uvs = mesh.TextureCoordinates.ToFloatArray();
                        var normals = mesh.Normals.ToFloatArray();
                        var faces = mesh.Faces.ToIntArray(true);

                        AbcWriterAddPolyMesh(instance, "/" + name,
                            materialName,
                            mesh.Vertices.ToFloatArray(), mesh.Vertices.Count * 3,
                            normals, normals.Length,
                            uvs, uvs.Length,
                            faces, faces.Length, faces.Length / 3, flip);
                    } else if (meshes[j] is Curve)
                    {
                        var curve = meshes[j] as Curve;
                        var results = ProcessCurve(curve, name);
                        var vertices = results.Item1;
                        var numVertsPerCurve = results.Item2;
                        var weights = results.Item3;
                        var knots = results.Item4;
                        var degrees = results.Item5;
                        AbcWriterAddCurveEx(instance, "/" + name,
                            vertices.ToArray(),
                            vertices.Count / 3,
                            numVertsPerCurve.ToArray(),
                            numVertsPerCurve.Count,
                            curve.IsPeriodic,
                            null, null, null, weights.ToArray(), degrees.ToArray(), knots.ToArray(), flip);
                    }
                }

                AbcWriterClose(instance);

                if (socket != null) {
                    var payload = new Payload
                    {
                        Action = "update",
                        FilePath = path,
                        ObjectNames = names,
                        CollectionName = collectionName
                    };
                    var jsonString = JsonConvert.SerializeObject(payload);
                    socket.Send(jsonString);
                }
            };

            //action();
            // Finally assign the spiral to the output parameter.
            debouncer.Debounce(action);
        }

        /// <summary>
        /// The Exposure property controls where in the panel a component icon 
        /// will appear. There are seven possible locations (primary to septenary), 
        /// each of which can be combined with the GH_Exposure.obscure flag, which 
        /// ensures the component will only be visible on panel dropdowns.
        /// </summary>
        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.primary; }
        }

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                // You can add image files to your project resources and access them like this:
                //return Resources.IconForThisComponent;
                return null;
            }
        }

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("c3d04175-545f-428f-9ffb-e13ebec1d265"); }
        }
    };

    [JsonObject(MemberSerialization.OptIn)]
    public class Payload
    {
        [JsonProperty(PropertyName = "action")]
        public string Action { get; set; }

        [JsonProperty(PropertyName = "filepath")]
        public string FilePath { get; set; }

        [JsonProperty(PropertyName = "collectionName")]
        public string CollectionName { get; set; }

        [JsonProperty(PropertyName = "objectNames")]
        public List<string> ObjectNames { get; set; }
    };
}
