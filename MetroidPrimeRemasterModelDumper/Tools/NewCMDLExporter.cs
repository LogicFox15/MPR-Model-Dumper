using AvaloniaToolbox.Core;
using DKCTF;
using IONET;
using IONET.Collada.Core.Geometry;
using IONET.Collada.Core.Scene;
using IONET.Core;
using IONET.Core.Model;
using IONET.Core.Skeleton;
using RetroStudioPlugin.Files.FileData;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

#nullable disable

namespace EvilWithin2Tool
{
    public class NewCMDLExporter
    {
        /*
        string MainGLTF = "{";
        public static void Export(CMDL cmdl, string path)
        {
            string ExportData = "MetaData: ";
            ExportData += "GPU Offset: " + cmdl.Meta.GPUOffset;
            ExportData += System.Environment.NewLine;
            ExportData += System.Environment.NewLine;

            ExportData += "Render Mesh Infos: ";
            ExportData += System.Environment.NewLine;

            for (int i = 0; i < cmdl.Meshes.Count; i++)
            {
                ExportData += "Render Mesh Entry: " + i + System.Environment.NewLine;
                ExportData += "Material Index: " + cmdl.Meshes[i].Header.MaterialIndex.ToString("X4") + System.Environment.NewLine;
                ExportData += "Vertex Buffer Index: " + cmdl.Meshes[i].Header.VertexBufferIndex.ToString("X2") + System.Environment.NewLine;
                ExportData += "Index Buffer Index: " + cmdl.Meshes[i].Header.IndexBufferIndex.ToString("X2") + System.Environment.NewLine;
                ExportData += "Index Start: " + cmdl.Meshes[i].Header.IndexStart.ToString("X8") + System.Environment.NewLine;
                ExportData += "Index Count: " + cmdl.Meshes[i].Header.IndexCount.ToString("X8") + System.Environment.NewLine;
                ExportData += System.Environment.NewLine;
            }


            ExportData += "Read Buffer Infos: ";
            ExportData += System.Environment.NewLine;

            for (int i = 0; i < cmdl.Meta.ReadBufferInfo.Count; i++)
            {
                ExportData += "Size: " + cmdl.Meta.ReadBufferInfo[i].Size.ToString("X8");
                ExportData += System.Environment.NewLine;
                ExportData += "Offset: " + cmdl.Meta.ReadBufferInfo[i].Offset.ToString("X8");
                ExportData += System.Environment.NewLine;
            }

            // List out the Vertex Data Components
            ExportData += System.Environment.NewLine;
            ExportData += "Vertex Data Components: ";
            ExportData += System.Environment.NewLine;

            for (int i = 0; i < cmdl.VertexBuffers.Count; i++)
            {
                ExportData += "Vertex Data Component Entry: " + i + System.Environment.NewLine;
                for (int c = 0; c < cmdl.VertexBuffers[i].Components.Count; c++)
                {
                    ExportData += "Component Entry: " + c + System.Environment.NewLine;
                    ExportData += "Buffer ID: " + cmdl.VertexBuffers[i].Components[c].BufferID.ToString() + System.Environment.NewLine;
                    ExportData += "Offset: " + cmdl.VertexBuffers[i].Components[c].Offset.ToString("X8") + System.Environment.NewLine;
                    ExportData += "Stride: " + cmdl.VertexBuffers[i].Components[c].Stride.ToString("X8") + System.Environment.NewLine;
                    ExportData += "Component Entry: " + cmdl.VertexBuffers[i].Components[c].Format.ToString() + System.Environment.NewLine;
                    ExportData += "Component Entry: " + cmdl.VertexBuffers[i].Components[c].Type.ToString() + System.Environment.NewLine;
                    ExportData += System.Environment.NewLine;
                    //ExportData += System.Environment.NewLine;
                }

            }

            ExportData += System.Environment.NewLine;
            ExportData += "Index Buffer Data:";
            ExportData += System.Environment.NewLine;

            foreach (var data in cmdl.IndexBytes)
            {
                string currentData = ByteArrayToString(data);
                currentData = SpliceText(currentData, 32);
                currentData = SpaceText(currentData, 8);

                currentData += System.Environment.NewLine;
                ExportData += currentData;
            }

            ExportData += System.Environment.NewLine;
            ExportData += "Vertex Buffer Data:";
            ExportData += System.Environment.NewLine;

            foreach (var data in cmdl.VertexBytes)
            {
                string currentData = ByteArrayToString(data);
                currentData = SpliceText(currentData, 32);
                currentData = SpaceText(currentData, 8);

                currentData += System.Environment.NewLine;
                ExportData += currentData;
            }

            File.WriteAllText(path + ".txt", ExportData);
        }

        public static string ByteArrayToString(byte[] ba)
        {
            StringBuilder hex = new StringBuilder(ba.Length * 2);
            foreach (byte b in ba)
                hex.AppendFormat("{0:x2}", b);
            return hex.ToString();
        }

        public static string SpliceText(string text, int lineLength)
        {
            return Regex.Replace(text, "(.{" + lineLength + "})", "$1" + Environment.NewLine);
        }
        public static string SpaceText(string text, int lineLength)
        {
            return Regex.Replace(text, "(.{" + lineLength + "})", "$1" + " ");
        }
        */
    }
}
