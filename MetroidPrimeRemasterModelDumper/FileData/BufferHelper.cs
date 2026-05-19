using AvaloniaToolbox.Core.IO;
using AvaloniaToolbox.RenderBase;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace DKCTF
{
    /// <summary>
    /// Represents a helper for loading vertex and index buffer information.
    /// </summary>
    internal class BufferHelper
    {
        /// <summary>
        /// Gets an index list from the provided buffer and formatting info.
        /// </summary>
        public static uint[] LoadIndexBuffer(byte[] buffer, CMDL.IndexFormat format, bool isLittleEndian)
        {
            var stride = GetIndexStride(format);
            uint[] indices = new uint[buffer.Length / stride];

            using (var reader = new FileReader(buffer))
            {
                reader.SetByteOrder(!isLittleEndian); //switch is little endianness

                if (format == CMDL.IndexFormat.Uint16)
                {
                    for (int i = 0; i < indices.Length; i++)
                        indices[i] = reader.ReadUInt16();
                }
                else
                {
                    for (int i = 0; i < indices.Length; i++)
                        indices[i] = reader.ReadUInt32();
                }
            }
            return indices;
        }

        /// <summary>
        /// Gets a vertex list from the provided buffer and descriptor info.
        /// </summary>
        public static CMDL.CVertex[] LoadVertexBuffer(List<byte[]> buffers, int startIndex, CMDL.VertexBuffer vertexInfo, bool isLittleEndian, bool swapTexCoord)
        {
            int vertexCount = InferVertexCount(buffers, startIndex, vertexInfo);
            var vertices = new CMDL.CVertex[vertexCount];

            foreach (var comp in vertexInfo.Components)
            {
                Console.WriteLine($"comp {comp.Type} {comp.Format}");

                var buffer = buffers[startIndex + (int)comp.BufferID];
                using (var reader = new FileReader(buffer))
                {
                    reader.SetByteOrder(!isLittleEndian); // Switch endian

                    if (comp.Type == CMDL.EVertexComponent.in_texCoord1)
                    {
                        comp.Offset += 4;
                    }
                    if (comp.Type == CMDL.EVertexComponent.in_texCoord2)
                    {
                        comp.Offset += 8;
                    }
                    if (comp.Type == CMDL.EVertexComponent.in_texCoord3)
                    {
                        comp.Offset += 12;
                    }

                    Console.WriteLine("Offset: " + comp.Offset);


                    for (int i = 0; i < vertexCount; i++)
                    {
                        if (vertices[i] == null)
                            vertices[i] = new CMDL.CVertex();

                        CMDL.CVertex vertex = vertices[i];

                        try
                        {
                            reader.SeekBegin(comp.Offset + i * comp.Stride);
                        }
                        catch
                        {
                            break;
                        }

                        switch (comp.Type)
                        {
                            case CMDL.EVertexComponent.in_position:
                                vertex.Position = ReadData(reader, comp.Format).Xyz();
                                break;

                            case CMDL.EVertexComponent.in_normal:
                                vertex.Normal = ReadData(reader, comp.Format).Xyz();
                                break;

                            case CMDL.EVertexComponent.in_texCoord0:
                                vertex.TexCoord0 = ReadData(reader, comp.Format).Xy();
                                break;

                            case CMDL.EVertexComponent.in_texCoord1:
                                try
                                {
                                    vertex.TexCoord1 = ReadData(reader, comp.Format).Xy();
                                    break;
                                }
                                catch
                                {
                                    break;
                                }
                            case CMDL.EVertexComponent.in_texCoord2:
                                try
                                {
                                    vertex.TexCoord2 = ReadData(reader, comp.Format).Xy();
                                    break;
                                }
                                catch
                                {
                                    break;
                                }
                            case CMDL.EVertexComponent.in_texCoord3:
                                try
                                {
                                    vertex.TexCoord3 = ReadData(reader, comp.Format).Xy();
                                    break;
                                }
                                catch
                                {
                                    break;
                                }

                            case CMDL.EVertexComponent.in_boneWeights:
                                //Console.WriteLine("Found bone weights when processing models");
                                vertex.BoneWeights = ReadData(reader, comp.Format);
                                break;

                            case CMDL.EVertexComponent.in_boneIndices:
                                //Console.WriteLine("Found bone indices when processing models");
                                vertex.BoneIndices = ReadData(reader, comp.Format);
                                break;

                            case CMDL.EVertexComponent.in_color:
                                vertex.Color = ReadData(reader, comp.Format);
                                break;

                            case CMDL.EVertexComponent.in_tangent0:
                                vertex.Tangent = ReadData(reader, comp.Format);
                                break;
                        }
                    }
                }
            }

            return vertices;
        }

        private static int InferVertexCount(List<byte[]> buffers, int startIndex, CMDL.VertexBuffer vertexInfo)
        {
            int vertexCount = int.MaxValue;

            // Group components by buffer ID
            foreach (var group in vertexInfo.Components.GroupBy(c => c.BufferID))
            {
                var comp = group.First();
                var buffer = buffers[startIndex + (int)comp.BufferID];

                if (comp.Stride <= 0)
                    throw new InvalidOperationException("Invalid vertex stride");

                int count = buffer.Length / (int)comp.Stride;
                vertexCount = Math.Min(vertexCount, count);
            }

            if (vertexCount <= 0 || vertexCount == int.MaxValue)
                throw new InvalidOperationException("Failed to infer vertex count");

            return vertexCount;
        }

        static Vector4 ReadData(FileReader reader, CMDL.VertexFormat format)
        {
            switch (format)
            {
                case CMDL.VertexFormat.Format_16_16_HalfSingle: return new Vector4( // 20
                     (float)reader.ReadHalf(), (float)reader.ReadHalf(), 0, 0);
                case CMDL.VertexFormat.Format_32_32_32_Single: return new Vector4( // 37
                    reader.ReadSingle(), reader.ReadSingle(), 
                    reader.ReadSingle(), 0); 
                case CMDL.VertexFormat.Format_16_16_16_HalfSingle:  return new Vector4( // 34
                     (float)reader.ReadHalf(), (float)reader.ReadHalf(),
                     (float)reader.ReadHalf(), (float)reader.ReadHalf());
                case CMDL.VertexFormat.Format_8_8_8_8_UNorm: return new Vector4( // 21
                       (float)reader.ReadByte() / 255, (float)reader.ReadByte() / 255,
                       (float)reader.ReadByte() / 255, (float)reader.ReadByte() / 255);
                case CMDL.VertexFormat.Format_8_8_8_8_Uint: return new Vector4( // 22
                       reader.ReadByte(), reader.ReadByte(),
                       reader.ReadByte(), reader.ReadByte());
                case CMDL.VertexFormat.Format_32_32_32_32_Single: return new Vector4( // 40
                    reader.ReadSingle(), reader.ReadSingle(),
                    reader.ReadSingle(), reader.ReadSingle());
                case CMDL.VertexFormat.Format_16_16_16_16_UNorm: return new Vector4( // 30
                    (float)reader.ReadUInt16() / 65535f, (float)reader.ReadUInt16() / 65535f,
                    (float)reader.ReadUInt16() / 65535f, (float)reader.ReadUInt16() / 65535f);
            }
            return new Vector4();
        }

        private static int GetIndexStride(CMDL.IndexFormat format)
        {
            if (format == CMDL.IndexFormat.Uint32) return 4;
            else return 2;
        }
    }
}
