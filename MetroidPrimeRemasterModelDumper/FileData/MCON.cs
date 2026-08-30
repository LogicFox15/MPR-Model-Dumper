using AvaloniaToolbox.Core.IO;
using DKCTF;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq.Expressions;
using System.Numerics;
using System.Xml.Linq;
using static RetroStudioPlugin.Files.FileData.CHPR;
using static RetroStudioPlugin.Files.FileData.CHPR.SBaseInfo;


namespace RetroStudioPlugin.Files.FileData
{
    public class MCON : FileForm
    {
        


        public MCON(System.IO.Stream stream) : base(stream)
        {
        }

        public override void Read(FileReader reader)
        {
            reader.SetByteOrder(false);
            
        }

        // MCHD
        // MCVD
        // MCCD
        // MCRC
        // PEEK


        public class ModConHeader
        {

        }

        public class ModConVisualData
        {

        }

        public class CCollisionTree
        {

        }

        public class ModConRC
        {

        }

        public class ModConVertexBlendData
        {

        }

    }

}

