using System;
using System.IO;
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

        protected void ProcessCurve(Curve geom, string name, bool flip)
        {
            List<float> vertices = new List<float>();
            bool isPeriodic = false;
            int pointCount = 0;
            int degree = 0;

            if (geom is PolyCurve)
            {
                var curve = geom as PolyCurve;
                for (var i = 0; i < curve.SegmentCount; i++)
                {
                    var seg = curve.SegmentCurve(i);
                    ProcessCurve(seg, name + "-segment-" + i, flip);
                }
            }
            else if (geom is NurbsCurve)
            {
                var curve = geom as NurbsCurve;
                var points = curve.Points;
                for (var i = 0; i < points.Count; i++)
                {
                    vertices.Add((float)points[i].X);
                    vertices.Add((float)points[i].Y);
                    vertices.Add((float)points[i].Z);
                }
                pointCount = points.Count;
                isPeriodic = curve.IsPeriodic;
                degree = curve.Degree;
            } else if (geom is LineCurve)
            {
                var curve = geom as LineCurve;
                
                vertices.Add((float)curve.PointAtStart.X);
                vertices.Add((float)curve.PointAtStart.Y);
                vertices.Add((float)curve.PointAtStart.Z);
                vertices.Add((float)curve.PointAtEnd.X);
                vertices.Add((float)curve.PointAtEnd.Y);
                vertices.Add((float)curve.PointAtEnd.Z);

                pointCount = 2;
                isPeriodic = curve.IsPeriodic;
                degree = curve.Degree;

            } else if (geom is PolylineCurve)
            {
                var curve = geom as PolylineCurve;

                for (var i = 0; i < curve.PointCount; i++)
                {
                    var p = curve.Point(i);
                    vertices.Add((float)p.X);
                    vertices.Add((float)p.Y);
                    vertices.Add((float)p.Z);
                }

                pointCount = curve.PointCount;
                isPeriodic = curve.IsPeriodic;
                degree = curve.Degree;

            }
            else if (geom is ArcCurve)
            {
                var arc = geom as ArcCurve;
                var c = geom.ToNurbsCurve();
                
                var center = arc.Arc.Center;
                var radius = arc.Arc.Radius;

                var points = new List<double>();
                var weights = new List<double>();
                for (var i = 0; i < c.Points.Count; i++)
                {
                    var p = new Point3d();
                    if (i % 2 == 0)
                    {
                        points.Add(c.Points[i].X - p.X);
                        points.Add(c.Points[i].Y - p.Y);
                        points.Add(c.Points[i].Z - p.Z);
                    }
                    else
                    {
                        p = arc.Arc.Center;
                        p.X *= 1.0 - c.Points[i].Weight;
                        p.Y *= 1.0 - c.Points[i].Weight;
                        p.Z *= 1.0 - c.Points[i].Weight;
                        points.Add(c.Points[i].X + p.X);
                        points.Add(c.Points[i].Y + p.Y);
                        points.Add(c.Points[i].Z + p.Z);
                    }
                    weights.Add(c.Points[i].Weight);
                }

                var cc = new NurbsCurve(c.Degree, c.Points.Count - 1);
                for (var i = 0; i < cc.Points.Count; i++)
                {
                    var w = 1.0;
                    cc.Points.SetPoint(i, points[i * 3 + 0], points[i * 3 + 1], points[i * 3 + 2], w);
                }
                // @TODO: use the proper api from Alembic to set the weights

                //if (arc.IsClosed)
                //{
                //    var cc = new NurbsCurve(3, 8);

                //    for (var i = 0; i < cc.Points.Count; i++)
                //    {
                //        var phase = (double)i / (double)(cc.Points.Count) * Math.PI * 2.0;
                //        var _x = Math.Cos(phase);
                //        var _y = Math.Sin(phase);
                //        var weight = 1.0;
                //        if (i % 2 == 1)
                //        {
                //            _x *= Math.Sqrt(2.0);
                //            _y *= Math.Sqrt(2.0);
                //            weight = 1.0;
                //        }
                //        var p = new Point3d(_x, _y, 0) * arc.Arc.Radius;
                //        p = (Point3d)(arc.Arc.Plane.XAxis * p.X + arc.Arc.Plane.YAxis * p.Y + arc.Arc.Plane.ZAxis * p.Z);
                //        p += arc.Arc.Center;
                //        cc.Points.SetPoint(i, p);
                //        cc.Points.SetWeight(i, weight);
                //    }
                //    ProcessCurve(cc, name + "_closed", flip);
                //}

                ProcessCurve(cc, name + (arc.IsClosed ? "_closed" : ""), flip);
            }
           

            if (vertices.Count > 0)
            {
                AbcWriterAddCurve(instance, "/" + name + "_d" + degree, vertices.ToArray(), pointCount, degree, isPeriodic, flip);
            }
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
                        ProcessCurve(meshes[j] as Curve, name, flip);
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
