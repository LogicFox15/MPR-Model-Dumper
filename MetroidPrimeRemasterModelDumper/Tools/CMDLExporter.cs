using AvaloniaToolbox.Core;
using DKCTF;
using IONET;
using IONET.Collada.Core.Geometry;
using IONET.Collada.Core.Scene;
using IONET.Collada.Core.Transform;
using IONET.Core;
using IONET.Core.Model;
using IONET.Core.Skeleton;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using MetroidPrimeRemasterModelDumper;
using IONET.Core.Animation;

#nullable disable

namespace EvilWithin2Tool
{
    public class CMDLExporter
    {
        /// <summary>
        /// Exports a standalone CHPR rig used as the shared target for that character
        /// project's animation clips. Individual skin exports can keep their own rig
        /// but omit the duplicated baked animation tracks.
        /// </summary>
        public static void ExportRig(string path, CHPR charProject)
        {
            if (charProject == null || charProject.CharacterInfos.Count == 0)
                throw new ArgumentException("Cannot export a shared rig without CHPR CharacterInfo.", nameof(charProject));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A shared-rig output path is required.", nameof(path));

            CHPR.CharacterInfo characterInfo = charProject.CharacterInfos[0];
            IOScene scene = new IOScene();
            IOModel model = new IOModel();
            scene.Models.Add(model);

            Console.WriteLine($"[CHPR] Preparing shared rig hierarchy: {characterInfo.Bones.Count} bones.");
            List<IOBone> bones = BuildBones(characterInfo);
            Console.WriteLine($"[CHPR] Shared rig hierarchy prepared: {bones.Count} nodes.");

            foreach (IOBone bone in bones)
            {
                if (bone.Parent == null)
                    model.Skeleton.RootBones.Add(bone);
            }

            string outputFile = path.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase)
                ? path
                : path + ".gltf";
            string outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputFile));
            if (!string.IsNullOrEmpty(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            Console.WriteLine($"[CHPR] Writing shared animation rig: bones={bones.Count}, output={outputFile}");
            // Preserve every hierarchy node in this mesh-free file; no mesh optimization is needed.
            IOManager.ExportScene(scene, outputFile, new ExportSettings
            {
                Optimize = false
            });
            Console.WriteLine($"[CHPR] Shared animation rig written: {outputFile}");
        }

        private static List<IOBone> BuildBones(CHPR.CharacterInfo characterInfo)
        {
            var bones = new List<IOBone>(characterInfo.Bones.Count);
            foreach (CHPR.BoneData bone in characterInfo.Bones)
            {
                if (!Matrix4x4.Decompose(bone.LocalTransform, out Vector3 scale,
                        out Quaternion rotation, out Vector3 translation))
                {
                    scale = Vector3.One;
                    rotation = Quaternion.Identity;
                    translation = Vector3.Zero;
                }

                bones.Add(new IOBone
                {
                    Name = bone.Name,
                    Rotation = rotation,
                    Scale = scale,
                    Translation = translation
                });
            }

            // Preserve the original first-match behavior for duplicate names while making
            // parent lookup linear rather than scanning the full skeleton for every bone.
            var firstBoneByName = new Dictionary<string, IOBone>(StringComparer.Ordinal);
            foreach (IOBone bone in bones)
            {
                // Match string equality from the previous FirstOrDefault implementation:
                // null names never matched an empty parent-name string.
                if (bone.Name == null)
                    continue;

                if (!firstBoneByName.ContainsKey(bone.Name))
                    firstBoneByName.Add(bone.Name, bone);
            }

            for (int i = 0; i < bones.Count; i++)
            {
                string parentName = characterInfo.Bones[i].Parent;
                if (parentName == null)
                    continue;

                if (firstBoneByName.TryGetValue(parentName, out IOBone parent) &&
                    !ReferenceEquals(parent, bones[i]))
                {
                    parent.AddChild(bones[i]);
                }
            }

            return bones;
        }

        public static void Export(CMDL cmdl, string path, CHPR charProject = null, bool saveLODs = false, bool appendAnimations = true)
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
                List<IOBone> iobones = BuildBones(charProject.CharacterInfos[0]);
                foreach (IOBone iobone in iobones)
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

            List<CMDL.CMesh> ExportMeshes;

            if (saveLODs)
            {
                ExportMeshes = cmdl.Meshes;
            }
            else
            {
                ExportMeshes = cmdl.GetHighestLODMeshes();
            }


            int MatName = 0;
            int meshnum = 0;
            foreach (var mesh in ExportMeshes)
            {
                //var mat = cmdl.Materials[mesh.Header.MaterialIndex];

                IOMesh iomesh = new IOMesh();

                //iomesh.Name = $"Mesh{iomodel.Meshes.Count}_{mat.Name}";
                string lodLevels = mesh.LODs.Count > 0 ? string.Join("_", mesh.LODs) : "None";

                iomesh.Name = $"Mesh{iomodel.Meshes.Count}_LOD{lodLevels}_MatID{mesh.Header.MaterialIndex}";

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

                    if (!mesh.hasTexCoord1)
                    {
                        iovertex.SetUV(vert.TexCoord1.X, vert.TexCoord1.Y, 3);
                    }
                    else
                    {
                        iovertex.SetUV(vert.TexCoord1.X, vert.TexCoord1.Y, 1);
                        iovertex.SetUV(vert.TexCoord2.X, vert.TexCoord2.Y, 2);

                        iovertex.SetUV(vert.TexCoord3.X, vert.TexCoord3.Y, 3);
                    }

                    iovertex.SetColor(
                        vert.Color1.X,
                        vert.Color1.Y,
                        vert.Color1.Z,
                        vert.Color1.W, 0);

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

            // The static model is written first. CompStream animations are appended
            // afterward so a decoder failure does not prevent the model export.
            if (charProject != null && appendAnimations)
            {
                try
                {
                    bool appended = CHPRAnimationExporter.AppendToGltf(path + ".gltf", charProject, out string animationReport);
                    Console.WriteLine("[CHPR] " + animationReport);
                    if (!appended)
                        Console.WriteLine("[CHPR] Static model export remains available without baked animation channels.");
                }
                catch (Exception animationEx)
                {
                    Console.WriteLine("[CHPR] Animation export failed without aborting static model export: " +
                        animationEx.GetType().Name + ": " + animationEx.Message);
                }
            }

            // Temporary kill switch
            //throw new Exception("Exported a rig, hopefully");

        }
    }
}
