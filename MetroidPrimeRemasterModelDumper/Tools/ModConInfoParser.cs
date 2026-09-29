using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Numerics;
using System.Security.Cryptography.X509Certificates;
using System.Diagnostics;
using AvaloniaToolbox.Core.IO;
using DKCTF;
using RetroStudioPlugin.Files.FileData;
#nullable disable

public class ModConParser
{
    public static string ModConInfo = "Model Container Information: ";

    public static void ParseModCon(MCON modcon, string path)
    {
        ModConInfo += System.Environment.NewLine + "Total Unique Objects 1: " + modcon.data.visualData.modelIdCount;
        ModConInfo += System.Environment.NewLine + "Total Unique Objects 2: " + modcon.data.visualData.worldModelCount;
        ModConInfo += System.Environment.NewLine + "Total Colors: " + modcon.data.visualData.colorCount;
        ModConInfo += System.Environment.NewLine + "Total Object Transforms Type 1: " + modcon.data.visualData.transformCount;
        ModConInfo += System.Environment.NewLine + "Total Object Transforms Type 2: " + modcon.data.visualData.worldInstanceCount;
        ModConInfo += System.Environment.NewLine;

        List<int> totals = new List<int>();

        for(int i = 0; i < modcon.data.visualData.modelIdCount; i++)
        {
            int occurrence = modcon.data.visualData.modelIndex.Count(x => x == i);
            totals.Add(occurrence);
            ModConInfo += System.Environment.NewLine + "Object Totals " + i + ": " + occurrence.ToString();
        }

        ModConInfo += System.Environment.NewLine;

        if (modcon.data.visualData.modelIdCount > 0)
        {
            ModConInfo += System.Environment.NewLine + "Unique IDs: ";
            for (int i = 0; i < modcon.data.visualData.modelIdCount; i++)
            {
                ModConInfo += System.Environment.NewLine + "Model ID " + i + ": " + modcon.data.visualData.modelID[i].ToString();
            }
            ModConInfo += System.Environment.NewLine;
        }

        if (modcon.data.visualData.worldModelCount > 0)
        {
            ModConInfo += System.Environment.NewLine + "Unique IDs 2: ";
            for (int i = 0; i < modcon.data.visualData.worldModelCount; i++)
            {
                ModConInfo += System.Environment.NewLine + "Model ID " + i + ": " + modcon.data.visualData.worldModelID[i].ToString();
            }
            ModConInfo += System.Environment.NewLine;
        }

        if (modcon.data.visualData.colorCount > 0)
        {
            ModConInfo += System.Environment.NewLine + "Colors: ";
            for (int i = 0; i < modcon.data.visualData.colorCount; i++)
            {
                ModConInfo += System.Environment.NewLine + "Color " + i + ": ";
                ModConInfo += System.Environment.NewLine + "R" + modcon.data.visualData.color[i].R.ToString();
                ModConInfo += System.Environment.NewLine + "G" + modcon.data.visualData.color[i].G.ToString();
                ModConInfo += System.Environment.NewLine + "B" + modcon.data.visualData.color[i].B.ToString();
                ModConInfo += System.Environment.NewLine + "A" + modcon.data.visualData.color[i].A.ToString();
                ModConInfo += System.Environment.NewLine;
            }
        }

        if (modcon.data.visualData.transformCount > 0)
        {
            ModConInfo += System.Environment.NewLine + "Object Visual Data 1: ";
            for (int i = 0; i < modcon.data.visualData.transformCount; i++)
            {
                // Extract original transforms
                Vector3 rawPos = TransformDecomposition.GetPosition(modcon.data.visualData.xf[i]);
                Quaternion rawRot = TransformDecomposition.GetRotationQuaternion(modcon.data.visualData.xf[i]);
                Vector3 rawScale = TransformDecomposition.GetScale(modcon.data.visualData.xf[i]);

                // Convert to Blender space
                Vector3 position = BlenderCoordinateConverter.ToBlenderPosition(rawPos);
                Quaternion rotation = BlenderCoordinateConverter.ToBlenderRotation(rawRot);
                Vector3 scale = BlenderCoordinateConverter.ToBlenderScale(rawScale);

                ModConInfo += System.Environment.NewLine + "Object " + i + ": ";
                ModConInfo += System.Environment.NewLine + "Short Value: " + modcon.data.visualData.modelIndex[i].ToString() + ", ID: " + modcon.data.visualData.modelID[modcon.data.visualData.modelIndex[i]].ToString();
                ModConInfo += System.Environment.NewLine + $"Position: {position.X}, {position.Y}, {position.Z}";
                ModConInfo += System.Environment.NewLine + $"Rotation: W: {rotation.W}, X: {rotation.X}, Y: {rotation.Y}, Z: {rotation.Z}";
                ModConInfo += System.Environment.NewLine + $"Scale: {scale.X}, {scale.Y}, {scale.Z}";
                ModConInfo += System.Environment.NewLine;
            }
        }

        if (modcon.data.visualData.worldInstanceCount > 0)
        {
            ModConInfo += System.Environment.NewLine + "Object Visual Data 1: ";
            for (int i = 0; i < modcon.data.visualData.worldInstanceCount; i++)
            {
                Vector3 position = TransformDecomposition.GetPosition(modcon.data.visualData.worldInstance[i].xf);
                Quaternion rotation = TransformDecomposition.GetRotationQuaternion(modcon.data.visualData.worldInstance[i].xf);
                Vector3 scale = TransformDecomposition.GetScale(modcon.data.visualData.worldInstance[i].xf);

                ModConInfo += System.Environment.NewLine + "Object " + i + ": ";
                ModConInfo += System.Environment.NewLine + "Short Value: " + modcon.data.visualData.colorIndex[i].ToString() + ", ID: " + modcon.data.visualData.worldInstance[i].id.ToString();
                ModConInfo += (System.Environment.NewLine + "Position: " + position.X.ToString() + ", " + (-position.Z).ToString() + ", " + position.Y.ToString());
                ModConInfo += (System.Environment.NewLine + "Rotation: W: " + rotation.W.ToString() + ", X: " + rotation.X.ToString() + ", Y: " + (-rotation.Z).ToString() + ", Z: " + rotation.Y.ToString());
                ModConInfo += (System.Environment.NewLine + "Scale: " + scale.X.ToString() + ", " + (scale.Z).ToString() + ", " + scale.Y.ToString());
                ModConInfo += System.Environment.NewLine;
            }
        }

        File.WriteAllText(path + ".json", ModConInfo);
        ModConInfo = "Model Container Information: ";
    }


    public static class TransformDecomposition
    {
        public static Vector3 GetPosition(in CTransform4f xf)
        {
            return new Vector3(xf.M0.W, xf.M1.W, xf.M2.W);
        }

        public static Vector3 GetScale(in CTransform4f xf)
        {
            // Extract columns as basis vectors from Matrix4x4
            Vector3 xAxis = new Vector3(xf.M0.X, xf.M1.X, xf.M2.X);
            Vector3 yAxis = new Vector3(xf.M0.Y, xf.M1.Y, xf.M2.Y);
            Vector3 zAxis = new Vector3(xf.M0.Z, xf.M1.Z, xf.M2.Z);

            float sx = xAxis.Length();
            float sy = yAxis.Length();
            float sz = zAxis.Length();

            float det =
                xAxis.X * (yAxis.Y * zAxis.Z - zAxis.Y * yAxis.Z) -
                yAxis.X * (xAxis.Y * zAxis.Z - zAxis.Y * xAxis.Z) +
                zAxis.X * (xAxis.Y * yAxis.Z - yAxis.Y * xAxis.Z);

            if (det < 0) sx = -sx;

            return new Vector3(sx, sy, sz);
        }

        private static void ExtractNormalizedBasis(in CTransform4f xf, out Vector3 x, out Vector3 y, out Vector3 z)
        {
            x = new Vector3(xf.M0.X, xf.M1.X, xf.M2.X);
            y = new Vector3(xf.M0.Y, xf.M1.Y, xf.M2.Y);
            z = new Vector3(xf.M0.Z, xf.M1.Z, xf.M2.Z);

            float sx = x.Length();
            float sy = y.Length();
            float sz = z.Length();

            float det =
                x.X * (y.Y * z.Z - z.Y * y.Z) -
                y.X * (x.Y * z.Z - z.Y * x.Z) +
                z.X * (x.Y * y.Z - y.Y * x.Z);

            if (det < 0) sx = -sx;

            if (sx != 0f) x /= sx;
            if (sy != 0f) y /= sy;
            if (sz != 0f) z /= sz;
        }

        public static Quaternion GetRotationQuaternion(in CTransform4f xf)
        {
            ExtractNormalizedBasis(xf, out var x, out var y, out var z);

            Matrix4x4 m = new Matrix4x4(
                x.X, x.Y, x.Z, 0f,
                y.X, y.Y, y.Z, 0f,
                z.X, z.Y, z.Z, 0f,
                0f, 0f, 0f, 1f
            );

            return Quaternion.CreateFromRotationMatrix(m);
        }
    }
}


public static class BlenderCoordinateConverter
{
    public static Vector3 ToBlenderPosition(Vector3 pos)
    {
        return new Vector3(pos.X, -pos.Z, pos.Y);
    }

    public static Vector3 ToBlenderScale(Vector3 scale)
    {
        // Scale maps to the new axes identically
        return new Vector3(scale.X, scale.Z, scale.Y);
    }

    public static Quaternion ToBlenderRotation(Quaternion q)
    {
        // Axis swap for OpenGL (Y-up) to Blender (Z-up): X -> X, Y -> -Z, Z -> Y
        // Since both spaces are Right-Handed, we do NOT negate the components.
        return new Quaternion(q.X, -q.Z, q.Y, q.W);
    }
}