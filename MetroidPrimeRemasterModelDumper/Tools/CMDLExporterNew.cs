using AvaloniaToolbox.Core;
using AvaloniaToolbox.RenderBase;
using DKCTF;
using IONET;
using IONET.Core;
using IONET.Core.Model;
using IONET.Core.Skeleton;
using MetroidPrimeRemasterModelDumper;
using MetroidPrimeRemasterModelDumper.Tools;
using System.Numerics;
using System.Text;

#nullable disable

namespace EvilWithin2Tool
{
    public class CMDLExporterNew
    {
        public static void ExportRoom(ConstructedRoom room, string path, bool saveLODs = false)
        {
            List<MCON> mcons = new List<MCON>();

            HashSet<string> writtenMaterialFiles = new HashSet<string>();

            // Build each modcon found within the room
            foreach (var layer in room.layers)
            {
                if (layer.modCons.Count > 0)
                {
                    for (int i = 0; i < layer.modCons.Count; i++)
                    {
                        foreach (var prop in layer.modCons[i].modConProperties.properties)
                        {
                            if (prop.propertyId == 0xA8E2BA93)
                            {
                                FileEntry file = BatchPakExtractor.SearchForFile(prop.modularConstructionId.ToString());
                                var mcon = new MCON(file.FileData);
                                mcon.fileName = file.AssetEntry.FileID;
                                Console.WriteLine("Read a modcon");
                                mcons.Add(mcon);
                            }
                        }
                    }
                }
            }

            // Process each modcon
            for (int m = 0; m < mcons.Count; m++)
            {
                IOScene ioscene = new IOScene();
                List<CMDL> cmdls = new List<CMDL>();
                IOModel iomodel = new IOModel();

                // Build each unique CMDL file
                for (int i = 0; i < mcons[m].data.visualData.modelIdCount; i++)
                {
                    FileEntry file = BatchPakExtractor.SearchForFile(mcons[m].data.visualData.modelID[i].ToString());
                    var cmdl = new CMDL(file.FileData);
                    Console.WriteLine("Unpacked model " + file.AssetEntry.FileID.ToString());
                    cmdls.Add(cmdl);

                    string modelId = mcons[m].data.visualData.modelID[i].ToString();

                    if (writtenMaterialFiles.Add(modelId))
                    {
                        string materialPath = Path.Combine(path, modelId);
                        WriteMaterialTextFile(cmdl, materialPath);
                    }
                }

                // Each entry in the model-index array is one room-model instance. The corresponding entry in xf is that instance's transform.
                // This matches the current Retro MCON layout: modelIndex[i] selects a model from modelID[], while xf[i] contains that instance's transform.
                int instanceCount = Math.Min((int)mcons[m].data.visualData.modelIndexCount, (int)mcons[m].data.visualData.transformCount);

                if (mcons[m].data.visualData.modelIndexCount != mcons[m].data.visualData.transformCount)
                {
                    Console.WriteLine(
                        $"WARNING: MCON instance/index count mismatch: " +
                        $"modelIndexCount={mcons[m].data.visualData.modelIndexCount}, " +
                        $"transformCount={mcons[m].data.visualData.transformCount}");
                }

                Console.WriteLine(
                    $"MCON {mcons[m].fileName}: models={cmdls.Count}, instances={instanceCount}");

                for (int i = 0; i < instanceCount; i++)
                {
                    // Get the atlas lookup
                    SAtlasLookup? atlasLookup = null;
                    if (mcons[m].data.visualData.visualAtlasCount > 0)
                    {
                        if (i < mcons[m].data.visualData.visualAtlas.Count)
                        {
                            atlasLookup = GetVisualAtlasLookup(mcons[m], i);
                        }
                        else
                        {
                            Console.WriteLine(
                                $"WARNING: MCON {mcons[m].fileName} instance {i} has no visual atlas lookup.");
                        }
                    }

                    int modelIndex = mcons[m].data.visualData.modelIndex[i];

                    if ((uint)modelIndex >= (uint)cmdls.Count)
                    {
                        Console.WriteLine(
                            $"WARNING: MCON {mcons[m].fileName} instance {i} references invalid model index {modelIndex}.");
                        continue;
                    }

                    if (mcons[m].data.visualData.visualAtlasCount != 0 && mcons[m].data.visualData.visualAtlasCount != mcons[m].data.visualData.transformCount)
                    {
                        Console.WriteLine(
                            $"WARNING: MCON visual atlas count mismatch: " +
                            $"visualAtlasCount={mcons[m].data.visualData.visualAtlasCount}, " +
                            $"transformCount={mcons[m].data.visualData.transformCount}");
                    }

                    iomodel.Name = $"M{m}_A{i}";

                    var cmdlToBuild = cmdls[modelIndex];
                    BuildStaticModel(iomodel, cmdlToBuild, mcons[m].data.visualData.xf[i], false, atlasLookup);
                }

                ioscene.Models.Add(iomodel);

                Console.WriteLine(mcons[m].fileName.ToString());
                Console.WriteLine(mcons[m].data.visualData.transformCount);

                string folder = Path.Combine(path, mcons[m].fileName.ToString());
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                string newPath = Path.Combine(folder, mcons[m].fileName.ToString());

                IOManager.ExportScene(ioscene, newPath + ".gltf", new ExportSettings()
                {
                    Optimize = false
                });
            }

            WriteLightmapInfo(room, mcons, path);


        }

        public static void BuildStaticModel(IOModel iomodel, CMDL cmdl, CTransform4f transform, bool saveLODs, SAtlasLookup? atlasLookup = null)
        {
            // CTransform4f is a 3x4 transform whose translation is stored in
            // M0.W, M1.W and M2.W. System.Numerics uses the equivalent affine
            // representation with translation in M41, M42 and M43, so transpose
            // the 3x3 portion when constructing Matrix4x4.
            Matrix4x4 matrix = new Matrix4x4(
                transform.M0.X, transform.M1.X, transform.M2.X, 0.0f,
                transform.M0.Y, transform.M1.Y, transform.M2.Y, 0.0f,
                transform.M0.Z, transform.M1.Z, transform.M2.Z, 0.0f,
                transform.M0.W, transform.M1.W, transform.M2.W, 1.0f
            );

            Console.WriteLine($"M11: {matrix.M11}, M12: {matrix.M12}, M13: {matrix.M13}, M14: {matrix.M14}");
            Console.WriteLine($"M21: {matrix.M21}, M22: {matrix.M22}, M23: {matrix.M23}, M24: {matrix.M24}");
            Console.WriteLine($"M31: {matrix.M31}, M32: {matrix.M32}, M33: {matrix.M33}, M34: {matrix.M34}");
            Console.WriteLine($"M41: {matrix.M41}, M42: {matrix.M42}, M43: {matrix.M43}, M44: {matrix.M44}");
            Console.WriteLine("");

            List<CMDL.CMesh> ExportMeshes;

            if (saveLODs)
            {
                ExportMeshes = cmdl.Meshes;
            }
            else
            {
                ExportMeshes = cmdl.GetHighestLODMeshes();
            }

            foreach (var mesh in ExportMeshes)
            {
                var mat = cmdl.Materials[mesh.Header.MaterialIndex];

                IOMesh iomesh = new IOMesh();

                iomesh.Name = $"{iomodel.Name}_Mesh{iomodel.Meshes.Count}_{mat.Name}";
                iomodel.Meshes.Add(iomesh);

                foreach (var vert in mesh.Vertices)
                {
                    var iovertex = new IOVertex()
                    {
                        Position = new System.Numerics.Vector3(
                            vert.Position.X,
                            vert.Position.Y,
                            vert.Position.Z),
                        Normal = new System.Numerics.Vector3(
                            vert.Normal.X,
                            vert.Normal.Y,
                            vert.Normal.Z),
                        Tangent = new System.Numerics.Vector3(
                            vert.Tangent.X,
                            vert.Tangent.Y,
                            vert.Tangent.Z),
                    };

                    iomesh.Vertices.Add(iovertex);

                    iovertex.SetUV(vert.TexCoord0.X, vert.TexCoord0.Y, 0);
                    if (mesh.hasTexCoord1)
                    {
                        iovertex.SetUV(vert.TexCoord1.X, vert.TexCoord1.Y, 1);
                    }
                    if (mesh.hasTexCoord2)
                    {
                        iovertex.SetUV(vert.TexCoord2.X, vert.TexCoord2.Y, 2);
                    }

                    if (atlasLookup.HasValue)
                    {
                        Vector2 lightmapUV =
                            TransformLightmapUV(vert.TexCoord0, atlasLookup.Value);

                        iovertex.SetUV(
                            lightmapUV.X,
                            lightmapUV.Y,
                            4
                        );
                    }

                    iovertex.SetColor(
                        vert.Color1.X,
                        vert.Color1.Y,
                        vert.Color1.Z,
                        vert.Color1.W, 0);
                }

                IOPolygon iopoly = new IOPolygon();
                iomesh.Polygons.Add(iopoly);

                iopoly.MaterialName = mat.Name;


                // Bake the MCON instance transform directly into the mesh vertices.
                // This avoids depending on whether the IONET IOModel node transform
                // is preserved by its glTF exporter.
                iomesh.TransformVertices(matrix);

                for (int i = 0; i < mesh.Indices.Length; i++)
                    iopoly.Indicies.Add((int)mesh.Indices[i]);
            }
        }

        private static Vector2 TransformLightmapUV(Vector2 uv, SAtlasLookup lookup)
        {
            return new Vector2(
                uv.X * lookup.scale + lookup.offsetU,
                uv.Y * lookup.scale + lookup.offsetV
            );
        }

        private static SAtlasLookup? GetVisualAtlasLookup(MCON mcon, int placementIndex)
        {
            if (mcon?.data?.visualData == null)
                return null;

            var visualData = mcon.data.visualData;

            // Retrotool treats an empty atlas table as "atlas lookup disabled".
            if (visualData.visualAtlas == null || visualData.visualAtlas.Count == 0)
                return null;

            // The atlas table belongs to visual placement order.
            if (placementIndex < 0 ||
                placementIndex >= visualData.xf.Count ||
                placementIndex >= visualData.visualAtlas.Count)
                return null;

            SAtlasLookup lookup = visualData.visualAtlas[placementIndex];

            // Same validity rule used by Retrotool.
            if (float.IsNaN(lookup.scale) ||
                float.IsInfinity(lookup.scale) ||
                lookup.scale < 0.0f)
            {
                return null;
            }

            return lookup;
        }

        private static void WriteMaterialTextFile(CMDL cmdl, string path)
        {
            string materialTXT = "Texture IDs: ";

            List<CMDL.CMaterial> mats = new List<CMDL.CMaterial>();

            foreach (var mat in cmdl.Materials)
            {
                mats.Add(mat);
            }

            CMDL.CMaterial[] cleanMats = mats.Distinct().ToArray();

            foreach (var mat in cleanMats)
            {
                materialTXT += Environment.NewLine + "Material: " + mat.Name;

                foreach (var texture in mat.Textures)
                {
                    materialTXT += Environment.NewLine
                        + "UV Map: "
                        + texture.UsageInfo.Flags
                        + "     "
                        + texture.FileID;
                }

                foreach (var scalar in mat.Scalars)
                {
                    materialTXT += Environment.NewLine
                        + "Scalar Type: "
                        + scalar.Key
                        + "     Value: "
                        + scalar.Value;
                }

                foreach (var i in mat.Int)
                {
                    materialTXT += Environment.NewLine
                        + "Integer Type: "
                        + i.Key
                        + "     Value: "
                        + i.Value;
                }

                foreach (var i4 in mat.Int4)
                {
                    materialTXT += Environment.NewLine
                        + "Integer 4 Type: "
                        + i4.Key;

                    for (int j = 0; j < i4.Value.Length; j++)
                    {
                        materialTXT += Environment.NewLine + i4.Value[j];
                    }
                }

                foreach (var matrix in mat.Matrices)
                {
                    materialTXT += Environment.NewLine
                        + "Matrix Type: "
                        + matrix.Key;

                    materialTXT += Environment.NewLine
                        + matrix.Value[0] + ", "
                        + matrix.Value[1] + ", "
                        + matrix.Value[2] + ", "
                        + matrix.Value[3];

                    materialTXT += Environment.NewLine
                        + matrix.Value[4] + ", "
                        + matrix.Value[5] + ", "
                        + matrix.Value[6] + ", "
                        + matrix.Value[7];

                    materialTXT += Environment.NewLine
                        + matrix.Value[8] + ", "
                        + matrix.Value[9] + ", "
                        + matrix.Value[10] + ", "
                        + matrix.Value[11];

                    materialTXT += Environment.NewLine
                        + matrix.Value[12] + ", "
                        + matrix.Value[13] + ", "
                        + matrix.Value[14] + ", "
                        + matrix.Value[15];
                }

                foreach (var color in mat.Colors)
                {
                    materialTXT += Environment.NewLine
                        + "Color Type: "
                        + color.Key;

                    materialTXT += Environment.NewLine + "R: " + color.Value.R;
                    materialTXT += Environment.NewLine + "G: " + color.Value.G;
                    materialTXT += Environment.NewLine + "B: " + color.Value.B;
                    materialTXT += Environment.NewLine + "A: " + color.Value.A;
                }

                materialTXT += Environment.NewLine;
            }

            string directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path + ".txt", materialTXT);
        }

        private static void WriteLightmapInfo( ConstructedRoom room, List<MCON> mcons, string path)
        {
            StringBuilder text = new StringBuilder();

            text.AppendLine("ROOM LIGHTMAP INFORMATION");
            text.AppendLine("==========================");
            text.AppendLine();

            if (room.lightMapTxtr.IsZero())
            {
                text.AppendLine("Lightmap Texture ID: NONE");
            }
            else
            {
                text.AppendLine(
                    "Lightmap Texture ID: " +
                    room.lightMapTxtr);
            }

            text.AppendLine();

            if (room.lightMapIds.Count > 0)
            {
                text.AppendLine("ROOM Lightmap IDs:");

                for (int i = 0; i < room.lightMapIds.Count; i++)
                {
                    text.AppendLine(
                        $"  [{i}] {room.lightMapIds[i]}");
                }

                text.AppendLine();
            }

            foreach (var mcon in mcons)
            {
                text.AppendLine(
                    "MCON: " +
                    mcon.fileName);

                text.AppendLine(
                    $"  Instances: {mcon.data.visualData.transformCount}");

                text.AppendLine();

                int instanceCount = Math.Min(
                    (int)mcon.data.visualData.transformCount,
                    (int)mcon.data.visualData.modelIndexCount);

                for (int i = 0; i < instanceCount; i++)
                {
                    int modelIndex =
                        mcon.data.visualData.modelIndex[i];

                    text.AppendLine(
                        $"  Instance {i}:");

                    text.AppendLine(
                        $"    Model Index: {modelIndex}");

                    if (modelIndex >= 0 &&
                        modelIndex < mcon.data.visualData.modelID.Count)
                    {
                        text.AppendLine(
                            $"    Model ID: " +
                            mcon.data.visualData.modelID[modelIndex]);
                    }

                    if (mcon.data.visualData.visualAtlas.Count > i)
                    {
                        var atlas =
                            mcon.data.visualData.visualAtlas[i];

                        text.AppendLine(
                            $"    Atlas Offset U: {atlas.offsetU}");

                        text.AppendLine(
                            $"    Atlas Offset V: {atlas.offsetV}");

                        text.AppendLine(
                            $"    Atlas Scale: {atlas.scale}");

                        text.AppendLine(
                            $"    Atlas Unknown: {atlas.unkD}");

                        text.AppendLine(
                            $"    Lightmap UV: " +
                            $"UV' = UV * {atlas.scale} + " +
                            $"({atlas.offsetU}, {atlas.offsetV})");
                    }
                    else
                    {
                        text.AppendLine(
                            "    Atlas Lookup: NONE");
                    }

                    text.AppendLine();
                }

                text.AppendLine();
            }

            File.WriteAllText(
                Path.Combine(path, "LightmapInfo.txt"),
                text.ToString());
        }

    }
}
