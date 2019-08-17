using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using WebSocketSharp;

namespace MNML


{
    public class GhAlembicComponent : GH_Component
    {

        [DllImport("ghAlembic")] static extern IntPtr AbcWriterCreateInstance();
        [DllImport("ghAlembic")] static extern bool AbcWriterOpen(IntPtr instance, String filepath);
        [DllImport("ghAlembic")] static extern void AbcWriterClose(IntPtr instance);
        [DllImport("ghAlembic")] static extern void AbcWriterAddPolyMesh(IntPtr instance, String name,
            float[] vertices, int numVertices,
            float[] normals, int numNormals,
            float[] uvs, int numUVs,
            int[] faces, int numFaces, int numFaceCount, bool _flipAxis);


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
            pManager.AddMeshParameter("Mesh", "M", "Mesh", GH_ParamAccess.list);
            pManager.AddTextParameter("Object Names", "N", "Object Names", GH_ParamAccess.list);
            pManager.AddTextParameter("Output Path", "P", "Output Path", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Flip Axis", "F", "Map Rhino Z to OBJ Y", GH_ParamAccess.item, true);
            pManager.AddGenericParameter("Socket", "S", "Established Websocket Client", GH_ParamAccess.item);

            pManager[1].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;


            // If you want to change properties of certain parameters, 
            // you can use the pManager instance to access them by index:
            //pManager[0].Optional = true;
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

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // First, we need to retrieve all data from the input parameters.
            // We'll start by declaring variables and assigning them starting values.
            var meshes = new List<Mesh>();
            var objectNames = new List<String>();

            var flip = true;

            String path = "";


            if (!DA.GetDataList(0, meshes)) return;
            if (!DA.GetData(2, ref path)) return;
            if (!DA.GetData(4, ref socket)) { socket = null; }
            DA.GetDataList(1, objectNames);
            DA.GetData(3, ref flip);


            Action action = () =>
            {

                instance = AbcWriterCreateInstance();
                AbcWriterOpen(instance, path);
                for (int j = 0; j < meshes.Count; j++)
                {
                    var mesh = meshes[j];
                    var name = objectNames.Count > j ? objectNames[j] : ("object-" + j);

                    mesh.Faces.ConvertQuadsToTriangles();
                    mesh.Normals.ComputeNormals();

                    List<float> uvs = new List<float>();
                    List<float> normals = new List<float>();

                    foreach (var uv in mesh.TextureCoordinates)
                    {
                        uvs.Add(uv.X);
                        uvs.Add(uv.Y);
                    }

                    foreach (var normal in mesh.Normals)
                    {
                        normals.Add(normal.X);
                        normals.Add(normal.Y);
                        normals.Add(normal.Z);
                    }

                    AbcWriterAddPolyMesh(instance, "/" + name,
                        mesh.Vertices.ToFloatArray(), mesh.Vertices.Count * 3,
                        normals.ToArray(), normals.Count,
                        uvs.ToArray(), uvs.Count,
                        mesh.Faces.ToIntArray(true), mesh.Faces.Count * 3, mesh.Faces.Count, flip);
                }

                AbcWriterClose(instance);
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
    }
}
