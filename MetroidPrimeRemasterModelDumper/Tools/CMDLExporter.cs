using AvaloniaToolbox.Core;
using DKCTF;
using IONET;
using IONET.Collada.Core.Geometry;
using IONET.Collada.Core.Scene;
using IONET.Collada.Core.Transform;
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
using System.Threading.Tasks;
using MetroidPrimeRemasterModelDumper;

#nullable disable

namespace EvilWithin2Tool
{
    public class CMDLExporter
    {
        public static void Export(CMDL cmdl, string path, CHPR charProject = null, bool saveLODs = false)
        {
            //string vertexContents = "";
            IOScene ioscene = new IOScene();

            IOModel iomodel = new IOModel();
            ioscene.Models.Add(iomodel);

            List<IOMaterial> materials = new List<IOMaterial>();
            foreach (var mat in cmdl.Materials)
            {
                materials.Add(new IOMaterial()
                {
                    Name = mat.Name,
                    Label = mat.Name,
                });
            }
            ioscene.Materials.AddRange(materials);

            if (charProject != null)
            {
                List<IOBone> iobones = new List<IOBone>();
                foreach (var bone in charProject.CharacterInfos[0].Bones)
                {
                    Matrix4x4.Decompose(bone.LocalTransform,
                        out Vector3 scale,
                        out Quaternion rotation,
                        out Vector3 translation);

                    iobones.Add(new IOBone()
                    {
                        Name = bone.Name,
                        Rotation = rotation,
                        Scale = scale,
                        Translation = translation,
                    });
                }
                for (int i = 0; i < iobones.Count; i++)
                {
                    var parentName = charProject.CharacterInfos[0].Bones[i].Parent;

                    var ioparent = iobones.FirstOrDefault(x => x.Name == parentName);
                    if (ioparent != null)
                        ioparent.AddChild(iobones[i]);
                }

                foreach (var iobone in iobones)
                    if (iobone.Parent == null)
                        iomodel.Skeleton.RootBones.Add(iobone);
            }
            else
            {
                iomodel.Skeleton.RootBones.Add(new IOBone()
                {
                    Name = "Root",
                });
            }

            List<CMDL.CMesh> HighestLOD = cmdl.GetHighestLODMeshes();


            int MatName = 0;
            int meshnum = 0;
            foreach (var mesh in HighestLOD)
            {
                //var mat = cmdl.Materials[mesh.Header.MaterialIndex];

                IOMesh iomesh = new IOMesh();

                //iomesh.Name = $"Mesh{iomodel.Meshes.Count}_{mat.Name}";
                string lodLevels = mesh.LODs.Count > 0 ? string.Join("_", mesh.LODs) : "None";

                iomesh.Name = $"Mesh{iomodel.Meshes.Count}_LOD{lodLevels}";

                iomodel.Meshes.Add(iomesh);

                int vertnum = 0;
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
                        vert.Color.X,
                        vert.Color.Y,
                        vert.Color.Z,
                        vert.Color.W, 0);

                    for (int j = 0; j < 4; j++)
                    {
                        //vertexContents += Environment.NewLine + "Mesh " + meshnum + " Vert " + vertnum + " Weight Value 0: " + vert.BoneWeights[0] + " Weight Value 1: " + vert.BoneWeights[1] + " Weight Value 2: " + vert.BoneWeights[2] + " Weight Value 3: " + vert.BoneWeights[3];

                        if (vert.BoneWeights[j] == 0 || charProject == null)
                            continue;

                        var boneIdx = (int)vert.BoneIndices[j];
                        var boneName = charProject.CharacterInfos[0].SkinnedBones[boneIdx].Name;

                        iovertex.Envelope.Weights.Add(new IOBoneWeight()
                        {
                            BoneName = boneName,
                            Weight = vert.BoneWeights[j],
                        });
                    }

                    iovertex.Envelope.NormalizeByteType();
                    vertnum++;
                }

                IOPolygon iopoly = new IOPolygon();
                iomesh.Polygons.Add(iopoly);

                iopoly.MaterialName = "Material_" + MatName++;

                iomesh.TransformVertices(Matrix4x4.Identity);

                for (int i = 0; i < mesh.Indices.Length; i++)
                    iopoly.Indicies.Add((int)mesh.Indices[i]);
                meshnum++;
            }




            //****************************//
            //    MATERIAL INFO LOGGER    //
            //****************************//
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
                    materialTXT += (System.Environment.NewLine + "UV Map: " + texture.UsageInfo.Flags.ToString() + "     Type: " + mat.TextureIDs[count].ToString() + "     " + texture.FileID.ToString());
                    string parentName = BatchPakExtractor.LocateTextureParentPak(texture.FileID.ToString());
                    materialTXT += "     Location: " + parentName;
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


            string vertexBufferTXT = "Vertex Buffer Information: ";



            foreach (var VBuf in cmdl.VertexBuffers)
            {
                //vertexBufferTXT += (System.Environment.NewLine + "Vertex Count: " + VBuf.VertexCount);
                int j = 0;
                foreach (var Comp in VBuf.Components)
                {
                    vertexBufferTXT += System.Environment.NewLine + "Component " + j + ": ";
                    vertexBufferTXT += System.Environment.NewLine + "Buffer ID: " + Comp.BufferID.ToString();
                    vertexBufferTXT += System.Environment.NewLine + "Offset: " + Comp.Offset.ToString();
                    vertexBufferTXT += System.Environment.NewLine + "Stride: " + Comp.Stride.ToString();
                    vertexBufferTXT += System.Environment.NewLine + "Vertex Data Format: " + Comp.Format.ToString();
                    vertexBufferTXT += System.Environment.NewLine + "Vertex Component Type: " + Comp.Type.ToString();
                    vertexBufferTXT += System.Environment.NewLine;
                    j++;
                }
            }

            //File.WriteAllText(path + "_BufferDebugInfo.txt", vertexBufferTXT);






            //****************************//
            //  MATERIAL INFO LOGGER END  //
            //****************************//



            IOManager.ExportScene(ioscene, path + ".gltf", new ExportSettings()
            {
                Optimize = true,
            });

            // Temporary kill switch
            //throw new Exception("Exported a rig, hopefully");

        }
    }
}
