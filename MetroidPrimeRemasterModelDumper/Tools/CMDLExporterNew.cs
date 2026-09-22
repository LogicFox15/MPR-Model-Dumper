using AvaloniaToolbox.Core;
using AvaloniaToolbox.RenderBase;
using DKCTF;
using IONET;
using IONET.Collada.Core.Geometry;
using IONET.Collada.Core.Scene;
using IONET.Collada.Core.Transform;
using IONET.Core;
using IONET.Core.Model;
using IONET.Core.Skeleton;
using MetroidPrimeRemasterModelDumper;
using MetroidPrimeRemasterModelDumper.Tools;
using RetroStudioPlugin.Files.FileData;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static AvaloniaToolbox.Core.IO.STFileSaver;

#nullable disable

namespace EvilWithin2Tool
{
    public class CMDLExporterNew
    {
        public static void ExportRoom(ConstructedRoom room, string path, bool saveLODs = false)
        {
            List<MCON> mcons = new List<MCON>();

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
                    int modelIndex = mcons[m].data.visualData.modelIndex[i];

                    if ((uint)modelIndex >= (uint)cmdls.Count)
                    {
                        Console.WriteLine(
                            $"WARNING: MCON {mcons[m].fileName} instance {i} references invalid model index {modelIndex}.");
                        continue;
                    }

                    iomodel.Name = $"M{m}_A{i}";

                    var cmdlToBuild = cmdls[modelIndex];
                    BuildStaticModel(iomodel, cmdlToBuild, mcons[m].data.visualData.xf[i], false);

                    //ioscene.Models.Add(iomodel);
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



            
        }

        public static void BuildStaticModel(IOModel iomodel, CMDL cmdl, CTransform4f transform, bool saveLODs)
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

            // Move the logging outside the mesh loop and fix the row labels
            //Console.WriteLine($"M11: {transform.M0.X}, M12: {transform.M0.Y}, M13: {transform.M0.Z}, M14: {transform.M0.W}");
            //Console.WriteLine($"M21: {transform.M1.X}, M22: {transform.M1.Y}, M23: {transform.M1.Z}, M24: {transform.M1.W}");
            //Console.WriteLine($"M31: {transform.M2.X}, M32: {transform.M2.Y}, M33: {transform.M2.Z}, M34: {transform.M2.W}");
            //Console.WriteLine("");

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

        public void PrintMaterialTextFIle(CMDL cmdl, string path)
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
                materialTXT += (System.Environment.NewLine + "Material: " + mat.Name);
                foreach (var texture in mat.Textures)
                {
                    materialTXT += (System.Environment.NewLine + "UV Map: " + texture.UsageInfo.Flags.ToString() + "     " + texture.FileID.ToString());
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

            File.WriteAllText(path + ".txt", materialTXT);
        }
    }
}
