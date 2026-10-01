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
    public class MCONExporter
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
                        FileEntry file = BatchPakExtractor.SearchForFile(layer.modCons[i].modularConstructionId.ToString());
                        var mcon = new MCON(file.FileData);
                        mcon.fileName = file.AssetEntry.FileID;
                        Console.WriteLine("Read a modcon");
                        mcons.Add(mcon);
                    }
                }
            }

            // Process each modcon
            for (int m = 0; m < mcons.Count; m++)
            {
                IOScene ioscene = new IOScene();
                List<CMDL> cmdls = new List<CMDL>();
                IOModel iomodel = new IOModel();

                string folder = Path.Combine(path, mcons[m].fileName.ToString());
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }  

                // Build each unique CMDL file
                for (int i = 0; i < mcons[m].data.visualData.modelIdCount; i++)
                {
                    FileEntry file = BatchPakExtractor.SearchForFile(mcons[m].data.visualData.modelID[i].ToString());
                    var cmdl = new CMDL(file.FileData);
                    Console.WriteLine("Unpacked model " + file.AssetEntry.FileID.ToString());
                    cmdls.Add(cmdl);

                    string modelId = mcons[m].data.visualData.modelID[i].ToString();

                    string materialPath = Path.Combine(folder, "CMDL_" + modelId);
                    WriteMaterialTextFile(cmdl, materialPath);
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

                    iomodel.Name = $"M{m}_A{i}";

                    var cmdlToBuild = cmdls[modelIndex];
                    BuildStaticModel(iomodel, cmdlToBuild, mcons[m].data.visualData.xf[i], false, i, modelIndex,atlasLookup);
                }

                ioscene.Models.Add(iomodel);

                Console.WriteLine(mcons[m].fileName.ToString());
                Console.WriteLine(mcons[m].data.visualData.transformCount);

                string newPath = Path.Combine(folder, mcons[m].fileName.ToString());

                ModConParser.ParseModCon(mcons[m], newPath);

                IOManager.ExportScene(ioscene, newPath + ".gltf", new ExportSettings()
                {
                });
            }

            //WriteLightmapInfo(room, mcons, path);
        }

        public static void BuildStaticModel(IOModel iomodel, CMDL cmdl, CTransform4f transform, bool saveLODs, int assetNumber, int assetInstance, SAtlasLookup? atlasLookup = null)
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
                string matName;

                IOMesh iomesh = new IOMesh(); 
                if (cmdl.Materials.Count > 0)
                {
                    var mat = cmdl.Materials[mesh.Header.MaterialIndex];
                    iomesh.Name = $"M{iomodel.Meshes.Count}I{assetInstance}A{assetNumber}_{mat.Name}";
                    matName = mat.Name;
                }
                else
                {
                    var mat = cmdl.MaterialsNew[mesh.Header.MaterialIndex];
                    iomesh.Name = $"M{iomodel.Meshes.Count}I{assetInstance}A{assetNumber}_{mat.Name}";
                    matName = mat.Name;
                }
                
                string lodLevels = mesh.LODs.Count > 0 ? string.Join("_", mesh.LODs) : "None";

                //iomesh.Name = $"Mesh{iomodel.Meshes.Count}_LOD{lodLevels}_MatID{mesh.Header.MaterialIndex}";

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
                        iovertex.SetUV(vert.TexCoord2.X, vert.TexCoord2.Y, 2);
                    }

                    iovertex.SetColor(
                        vert.Color1.X,
                        vert.Color1.Y,
                        vert.Color1.Z,
                        vert.Color1.W, 0);
                }

                IOPolygon iopoly = new IOPolygon();
                iomesh.Polygons.Add(iopoly);

                iopoly.MaterialName = matName;

                // Bake the MCON instance transform directly into the mesh vertices.
                // This avoids depending on whether the IONET IOModel node transform
                // is preserved by its glTF exporter.
                iomesh.TransformVertices(matrix);

                for (int i = 0; i < mesh.Indices.Length; i++)
                    iopoly.Indicies.Add((int)mesh.Indices[i]);
            }
        }

        private static Vector2 TransformBakedAtlasUV(Vector2 sourceUV, SAtlasLookup lookup)
        {
            if (!float.IsFinite(lookup.scale) ||
                !float.IsFinite(lookup.offsetU) ||
                !float.IsFinite(lookup.offsetV) ||
                lookup.scale < 0.0f)
            {
                return sourceUV;
            }

            return new Vector2(
                sourceUV.X * lookup.scale + lookup.offsetU,
                sourceUV.Y * lookup.scale + lookup.offsetV);
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
            List<CMDL.CMaterialNew> matsNew = new List<CMDL.CMaterialNew>();

            if (cmdl.Materials.Count > 0)
            {
                foreach (var mat in cmdl.Materials)
                {
                    mats.Add(mat);
                }
            }

            if (cmdl.MaterialsNew.Count > 0)
            {
                foreach (var mat in cmdl.MaterialsNew)
                {
                    matsNew.Add(mat);
                }
            }

            CMDL.CMaterial[] cleanMats = mats.Distinct().ToArray();
            CMDL.CMaterialNew[] cleanMatsNew = matsNew.Distinct().ToArray();

            foreach (var mat in cleanMats)
            {
                int count = 0;
                materialTXT += (System.Environment.NewLine + "Material: " + mat.Name);
                foreach (var texture in mat.Textures)
                {
                    materialTXT += (System.Environment.NewLine + "UV Map: " + texture.textureTokenData.UsageInfo.Flags.ToString() + "     Type: " + texture.type.ToString() + "     " + texture.textureTokenData.FileID.ToString());
                    string parentName = BatchPakExtractor.LocateTextureParentPak(texture.textureTokenData.FileID.ToString());
                    materialTXT += "     Location: " + parentName;
                }

                foreach (var Complex in mat.ComplexTypeAs)
                {
                    materialTXT += (System.Environment.NewLine + "Complex Type 1: ");
                    if (Complex.hasTex1)
                    {
                        materialTXT += (System.Environment.NewLine + "UV Map: " + Complex.Texture1.UsageInfo.Flags.ToString() + "     " + Complex.Texture1.FileID.ToString());
                    }
                    if (Complex.hasTex2)
                    {
                        materialTXT += (System.Environment.NewLine + "UV Map: " + Complex.Texture2.UsageInfo.Flags.ToString() + "     " + Complex.Texture2.FileID.ToString());
                    }
                    if (Complex.hasTex3)
                    {
                        materialTXT += (System.Environment.NewLine + "UV Map: " + Complex.Texture3.UsageInfo.Flags.ToString() + "     " + Complex.Texture3.FileID.ToString());
                    }
                }

                foreach (var Complex in mat.ComplexTypeBs)
                {
                    materialTXT += (System.Environment.NewLine + "Complex Type B: ");
                    for (int i = 0; i < Complex.colors.Count; i++)
                    {
                        materialTXT += System.Environment.NewLine + "Color " + i + ": " + Complex.colors[i].R.ToString() + ", " + Complex.colors[i].G.ToString() + ", " + Complex.colors[i].B.ToString() + ", " + Complex.colors[i].A.ToString();
                    }
                }

                foreach (var scalar in mat.Scalars)
                {
                    materialTXT += (System.Environment.NewLine + "Scalar Type: " + scalar.Key + "     Value: " + scalar.Value.ToString());
                }

                foreach (var i in mat.Int)
                {
                    materialTXT += (System.Environment.NewLine + "Integer Type: " + i.Key + "     Value: " + i.Value.ToString());
                }

                foreach (var i4 in mat.Int4)
                {
                    materialTXT += (System.Environment.NewLine + "Integer 4 Type: " + i4.Key);
                    materialTXT += (System.Environment.NewLine + i4.Value[0]);
                    materialTXT += (System.Environment.NewLine + i4.Value[1]);
                    materialTXT += (System.Environment.NewLine + i4.Value[2]);
                    materialTXT += (System.Environment.NewLine + i4.Value[3]);
                }

                foreach (var matrix in mat.Matrices)
                {
                    materialTXT += (System.Environment.NewLine + "Matrix Type: " + matrix.Key);
                    materialTXT += (System.Environment.NewLine + matrix.Value[0].ToString() + ", " + matrix.Value[1].ToString() + ", " + matrix.Value[2].ToString() + ", " + matrix.Value[3].ToString());
                    materialTXT += (System.Environment.NewLine + matrix.Value[4].ToString() + ", " + matrix.Value[5].ToString() + ", " + matrix.Value[6].ToString() + ", " + matrix.Value[7].ToString());
                    materialTXT += (System.Environment.NewLine + matrix.Value[8].ToString() + ", " + matrix.Value[9].ToString() + ", " + matrix.Value[10].ToString() + ", " + matrix.Value[11].ToString());
                    materialTXT += (System.Environment.NewLine + matrix.Value[12].ToString() + ", " + matrix.Value[13].ToString() + ", " + matrix.Value[14].ToString() + ", " + matrix.Value[15].ToString());
                }

                foreach (var color in mat.Colors)
                {
                    materialTXT += (System.Environment.NewLine + "Color Type: " + color.Key);
                    materialTXT += (System.Environment.NewLine + "R: " + color.Value.R.ToString());
                    materialTXT += (System.Environment.NewLine + "G: " + color.Value.G.ToString());
                    materialTXT += (System.Environment.NewLine + "B: " + color.Value.B.ToString());
                    materialTXT += (System.Environment.NewLine + "A: " + color.Value.A.ToString());
                }


                materialTXT += System.Environment.NewLine;
            }

            foreach (var mat in cleanMatsNew)
            {
                materialTXT += (System.Environment.NewLine + "Material: " + mat.Name);
                foreach (var texture in mat.Textures)
                {
                    materialTXT += (System.Environment.NewLine + "UV Map: " + texture.unkUint.ToString() + "     Type: " + texture.type + " " + texture.FileID.ToString());
                    string parentName = BatchPakExtractor.LocateTextureParentPak(texture.FileID.ToString());
                    materialTXT += "     Location: " + parentName;
                }
                foreach (var Complex in mat.Complex)
                {
                    materialTXT += (System.Environment.NewLine + "Complex: ");
                    for (int i = 0; i < Complex.Colors.Count; i++)
                    {
                        materialTXT += System.Environment.NewLine + "Color " + i + ": " + Complex.Colors[i].R.ToString() + ", " + Complex.Colors[i].G.ToString() + ", " + Complex.Colors[i].B.ToString() + ", " + Complex.Colors[i].A.ToString();
                    }
                }
                foreach (var scalar in mat.Scalars)
                {
                    materialTXT += (System.Environment.NewLine + "Scalar Type: " + scalar.Key + "     Value: " + scalar.Value.ToString());
                }
                foreach (var color in mat.Colors)
                {
                    materialTXT += (System.Environment.NewLine + "Color Type: " + color.Key);
                    materialTXT += (System.Environment.NewLine + "R: " + color.Value.R.ToString());
                    materialTXT += (System.Environment.NewLine + "G: " + color.Value.G.ToString());
                    materialTXT += (System.Environment.NewLine + "B: " + color.Value.B.ToString());
                    materialTXT += (System.Environment.NewLine + "A: " + color.Value.A.ToString());
                }

                materialTXT += System.Environment.NewLine;
            }

            File.WriteAllText(path + ".txt", materialTXT);
        }
    }
}
