using System;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;

namespace MNML
{
    public class GhAlembicInfo : GH_AssemblyInfo
    {
        public override string Name
        {
            get
            {
                return "GhAlembic Info";
            }
        }
        public override Bitmap Icon
        {
            get
            {
                //Return a 24x24 pixel bitmap to represent this GHA library.
                return null;
            }
        }
        public override string Description
        {
            get
            {
                //Return a short string describing the purpose of this GHA library.
                return "";
            }
        }
        public override Guid Id
        {
            get
            {
                return new Guid("a14ca8a4-7b9b-4879-8852-589677b96418");
            }
        }

        public override string AuthorName
        {
            get
            {
                //Return a string identifying you or your company.
                return "";
            }
        }
        public override string AuthorContact
        {
            get
            {
                //Return a string representing your preferred contact details.
                return "";
            }
        }
    }
}
