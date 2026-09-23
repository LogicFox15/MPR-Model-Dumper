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
            var vertices = new CMDL.CVertex[vertexInfo.VertexCount];

            foreach (var comp in vertexInfo.Components)
            {
                var buffer = buffers[startIndex + (int)comp.BufferID];

                using (var reader = new FileReader(buffer))
                {
                    reader.SetByteOrder(!isLittleEndian);

                    uint trueOffset = comp.Offset;

                    for (int i = 0; i < vertexInfo.VertexCount; i++)
                    {
                        if (vertices[i] == null)
                            vertices[i] = new CMDL.CVertex();

                        CMDL.CVertex vertex = vertices[i];

                        reader.SeekBegin(
                            trueOffset +
                            i * comp.Stride
                        );

                        Vector4 rawData = ReadData(reader, comp.Format);

                        switch (comp.Type)
                        {
                            case CMDL.EVertexComponent.in_position:
                                vertex.Position = rawData.Xyz();
                                break;

                            case CMDL.EVertexComponent.in_normal:
                                vertex.Normal = rawData.Xyz();
                                break;

                            case CMDL.EVertexComponent.in_texCoord0:
                                vertex.TexCoord0 = rawData.Xy();

                                if (comp.Format ==
                                    CMDL.VertexFormat.Format_16_16_16_HalfSingle ||
                                    comp.Format ==
                                    CMDL.VertexFormat.Format_32_32_32_32_Single)
                                {
                                    vertex.hasTexCoord1 = true;
                                    vertex.TexCoord1 =
                                        new Vector2(rawData.Z, rawData.W);
                                }
                                break;

                            case CMDL.EVertexComponent.in_texCoord1:
                                vertex.hasTexCoord2 = true;
                                vertex.TexCoord2 = rawData.Xy();
                                break;

                            case CMDL.EVertexComponent.in_boneWeights:
                                vertex.BoneWeights = rawData;
                                break;

                            case CMDL.EVertexComponent.in_boneIndices:
                                vertex.BoneIndices = rawData;
                                break;

                            case CMDL.EVertexComponent.in_color:
                                vertex.Color1 = rawData;
                                break;

                            case CMDL.EVertexComponent.in_tangent0:
                                vertex.Tangent = rawData;
                                break;
                        }
                    }
                }
            }

            return vertices;
        }

        private static Vector4 ReadData(FileReader reader, CMDL.VertexFormat format)
        {
            switch (format)
            {
                // R8
                case CMDL.VertexFormat.R8_UNorm:
                    return new Vector4(
                        reader.ReadByte() / 255.0f,
                        0.0f,
                        0.0f,
                        0.0f);

                case CMDL.VertexFormat.R8_UInt:
                    return new Vector4(
                        reader.ReadByte(),
                        0.0f,
                        0.0f,
                        0.0f);

                case CMDL.VertexFormat.R8_SNorm:
                    {
                        sbyte value = unchecked((sbyte)reader.ReadByte());

                        return new Vector4(
                            MathF.Max(value / 127.0f, -1.0f),
                            0.0f,
                            0.0f,
                            0.0f);
                    }

                case CMDL.VertexFormat.R8_SInt:
                    {
                        sbyte value = unchecked((sbyte)reader.ReadByte());

                        return new Vector4(
                            value,
                            0.0f,
                            0.0f,
                            0.0f);
                    }

                // R16
                case CMDL.VertexFormat.R16_UNorm:
                    return new Vector4(
                        reader.ReadUInt16() / 65535.0f,
                        0.0f,
                        0.0f,
                        0.0f);

                case CMDL.VertexFormat.R16_UInt:
                    return new Vector4(
                        reader.ReadUInt16(),
                        0.0f,
                        0.0f,
                        0.0f);

                case CMDL.VertexFormat.R16_SNorm:
                    {
                        short value = unchecked((short)reader.ReadUInt16());

                        return new Vector4(
                            MathF.Max(value / 32767.0f, -1.0f),
                            0.0f,
                            0.0f,
                            0.0f);
                    }

                case CMDL.VertexFormat.R16_SInt:
                    {
                        short value = unchecked((short)reader.ReadUInt16());

                        return new Vector4(
                            value,
                            0.0f,
                            0.0f,
                            0.0f);
                    }

                case CMDL.VertexFormat.R16_Float:
                    return new Vector4(
                        (float)reader.ReadHalf(),
                        0.0f,
                        0.0f,
                        0.0f);

                // RG8
                case CMDL.VertexFormat.RG8_UNorm:
                    return new Vector4(
                        reader.ReadByte() / 255.0f,
                        reader.ReadByte() / 255.0f,
                        0.0f,
                        0.0f);

                case CMDL.VertexFormat.RG8_UInt:
                    return new Vector4(
                        reader.ReadByte(),
                        reader.ReadByte(),
                        0.0f,
                        0.0f);

                case CMDL.VertexFormat.RG8_SNorm:
                    {
                        sbyte x = unchecked((sbyte)reader.ReadByte());
                        sbyte y = unchecked((sbyte)reader.ReadByte());

                        return new Vector4(
                            MathF.Max(x / 127.0f, -1.0f),
                            MathF.Max(y / 127.0f, -1.0f),
                            0.0f,
                            0.0f);
                    }

                case CMDL.VertexFormat.RG8_SInt:
                    {
                        sbyte x = unchecked((sbyte)reader.ReadByte());
                        sbyte y = unchecked((sbyte)reader.ReadByte());

                        return new Vector4(
                            x,
                            y,
                            0.0f,
                            0.0f);
                    }

                // R32
                case CMDL.VertexFormat.R32_UInt:
                    return new Vector4(
                        reader.ReadUInt32(),
                        0.0f,
                        0.0f,
                        0.0f);

                case CMDL.VertexFormat.R32_SInt:
                    {
                        int value = unchecked((int)reader.ReadUInt32());

                        return new Vector4(
                            value,
                            0.0f,
                            0.0f,
                            0.0f);
                    }

                case CMDL.VertexFormat.R32_Float:
                    return new Vector4(
                        BitConverter.UInt32BitsToSingle(reader.ReadUInt32()),
                        0.0f,
                        0.0f,
                        0.0f);

                // RG16
                case CMDL.VertexFormat.RG16_UNorm:
                    return new Vector4(
                        reader.ReadUInt16() / 65535.0f,
                        reader.ReadUInt16() / 65535.0f,
                        0.0f,
                        0.0f);

                case CMDL.VertexFormat.RG16_UInt:
                    return new Vector4(
                        reader.ReadUInt16(),
                        reader.ReadUInt16(),
                        0.0f,
                        0.0f);

                case CMDL.VertexFormat.RG16_SNorm:
                    {
                        short x = unchecked((short)reader.ReadUInt16());
                        short y = unchecked((short)reader.ReadUInt16());

                        return new Vector4(
                            MathF.Max(x / 32767.0f, -1.0f),
                            MathF.Max(y / 32767.0f, -1.0f),
                            0.0f,
                            0.0f);
                    }

                case CMDL.VertexFormat.RG16_SInt:
                    {
                        short x = unchecked((short)reader.ReadUInt16());
                        short y = unchecked((short)reader.ReadUInt16());

                        return new Vector4(
                            x,
                            y,
                            0.0f,
                            0.0f);
                    }

                case CMDL.VertexFormat.RG16_Float:
                    return new Vector4(
                        (float)reader.ReadHalf(),
                        (float)reader.ReadHalf(),
                        0.0f,
                        0.0f);

                // RGBA8
                case CMDL.VertexFormat.Format_8_8_8_8_UNorm:
                    return new Vector4(
                        reader.ReadByte() / 255.0f,
                        reader.ReadByte() / 255.0f,
                        reader.ReadByte() / 255.0f,
                        reader.ReadByte() / 255.0f);

                case CMDL.VertexFormat.Format_8_8_8_8_Uint:
                    return new Vector4(
                        reader.ReadByte(),
                        reader.ReadByte(),
                        reader.ReadByte(),
                        reader.ReadByte());

                case CMDL.VertexFormat.RGBA8_SNorm:
                    {
                        sbyte r = unchecked((sbyte)reader.ReadByte());
                        sbyte g = unchecked((sbyte)reader.ReadByte());
                        sbyte b = unchecked((sbyte)reader.ReadByte());
                        sbyte a = unchecked((sbyte)reader.ReadByte());

                        return new Vector4(
                            MathF.Max(r / 127.0f, -1.0f),
                            MathF.Max(g / 127.0f, -1.0f),
                            MathF.Max(b / 127.0f, -1.0f),
                            MathF.Max(a / 127.0f, -1.0f));
                    }

                case CMDL.VertexFormat.RGBA8_SInt:
                    {
                        sbyte r = unchecked((sbyte)reader.ReadByte());
                        sbyte g = unchecked((sbyte)reader.ReadByte());
                        sbyte b = unchecked((sbyte)reader.ReadByte());
                        sbyte a = unchecked((sbyte)reader.ReadByte());

                        return new Vector4(r, g, b, a);
                    }

                // RGB10A2
                case CMDL.VertexFormat.RGB10A2_UNorm:
                    {
                        uint packed = reader.ReadUInt32();

                        uint r = packed & 0x3FF;
                        uint g = (packed >> 10) & 0x3FF;
                        uint b = (packed >> 20) & 0x3FF;
                        uint a = (packed >> 30) & 0x3;

                        return new Vector4(
                            r / 1023.0f,
                            g / 1023.0f,
                            b / 1023.0f,
                            a / 3.0f);
                    }

                case CMDL.VertexFormat.RGB10A2_UInt:
                    {
                        uint packed = reader.ReadUInt32();

                        uint r = packed & 0x3FF;
                        uint g = (packed >> 10) & 0x3FF;
                        uint b = (packed >> 20) & 0x3FF;
                        uint a = (packed >> 30) & 0x3;

                        return new Vector4(r, g, b, a);
                    }

                // RG32
                case CMDL.VertexFormat.RG32_UInt:
                    return new Vector4(
                        reader.ReadUInt32(),
                        reader.ReadUInt32(),
                        0.0f,
                        0.0f);

                case CMDL.VertexFormat.RG32_SInt:
                    {
                        int x = unchecked((int)reader.ReadUInt32());
                        int y = unchecked((int)reader.ReadUInt32());

                        return new Vector4(
                            x,
                            y,
                            0.0f,
                            0.0f);
                    }

                case CMDL.VertexFormat.RG32_Float:
                    return new Vector4(
                        BitConverter.UInt32BitsToSingle(reader.ReadUInt32()),
                        BitConverter.UInt32BitsToSingle(reader.ReadUInt32()),
                        0.0f,
                        0.0f);

                // RGBA16
                case CMDL.VertexFormat.RGBA16_UNorm:
                    return new Vector4(
                        reader.ReadUInt16() / 65535.0f,
                        reader.ReadUInt16() / 65535.0f,
                        reader.ReadUInt16() / 65535.0f,
                        reader.ReadUInt16() / 65535.0f);

                case CMDL.VertexFormat.RGBA16_UInt:
                    return new Vector4(
                        reader.ReadUInt16(),
                        reader.ReadUInt16(),
                        reader.ReadUInt16(),
                        reader.ReadUInt16());

                case CMDL.VertexFormat.RGBA16_SNorm:
                    {
                        short r = unchecked((short)reader.ReadUInt16());
                        short g = unchecked((short)reader.ReadUInt16());
                        short b = unchecked((short)reader.ReadUInt16());
                        short a = unchecked((short)reader.ReadUInt16());

                        return new Vector4(
                            MathF.Max(r / 32767.0f, -1.0f),
                            MathF.Max(g / 32767.0f, -1.0f),
                            MathF.Max(b / 32767.0f, -1.0f),
                            MathF.Max(a / 32767.0f, -1.0f));
                    }

                case CMDL.VertexFormat.RGBA16_SInt:
                    {
                        short r = unchecked((short)reader.ReadUInt16());
                        short g = unchecked((short)reader.ReadUInt16());
                        short b = unchecked((short)reader.ReadUInt16());
                        short a = unchecked((short)reader.ReadUInt16());

                        return new Vector4(r, g, b, a);
                    }

                case CMDL.VertexFormat.Format_16_16_16_HalfSingle:
                    return new Vector4(
                        (float)reader.ReadHalf(),
                        (float)reader.ReadHalf(),
                        (float)reader.ReadHalf(),
                        (float)reader.ReadHalf());

                // RGB32
                case CMDL.VertexFormat.RGB32_UInt:
                    return new Vector4(
                        reader.ReadUInt32(),
                        reader.ReadUInt32(),
                        reader.ReadUInt32(),
                        0.0f);

                case CMDL.VertexFormat.RGB32_SInt:
                    {
                        int r = unchecked((int)reader.ReadUInt32());
                        int g = unchecked((int)reader.ReadUInt32());
                        int b = unchecked((int)reader.ReadUInt32());

                        return new Vector4(r, g, b, 0.0f);
                    }

                case CMDL.VertexFormat.Format_32_32_32_Single:
                    return new Vector4(
                        BitConverter.UInt32BitsToSingle(reader.ReadUInt32()),
                        BitConverter.UInt32BitsToSingle(reader.ReadUInt32()),
                        BitConverter.UInt32BitsToSingle(reader.ReadUInt32()),
                        0.0f);

                // RGBA32
                case CMDL.VertexFormat.RGBA32_UInt:
                    return new Vector4(
                        reader.ReadUInt32(),
                        reader.ReadUInt32(),
                        reader.ReadUInt32(),
                        reader.ReadUInt32());

                case CMDL.VertexFormat.RGBA32_SInt:
                    {
                        int r = unchecked((int)reader.ReadUInt32());
                        int g = unchecked((int)reader.ReadUInt32());
                        int b = unchecked((int)reader.ReadUInt32());
                        int a = unchecked((int)reader.ReadUInt32());

                        return new Vector4(r, g, b, a);
                    }

                case CMDL.VertexFormat.Format_32_32_32_32_Single:
                    return new Vector4(
                        BitConverter.UInt32BitsToSingle(reader.ReadUInt32()),
                        BitConverter.UInt32BitsToSingle(reader.ReadUInt32()),
                        BitConverter.UInt32BitsToSingle(reader.ReadUInt32()),
                        BitConverter.UInt32BitsToSingle(reader.ReadUInt32()));

                default:
                    throw new NotSupportedException(
                        $"Unsupported MPR vertex format: {(int)format}");
            }
        }

        private static int GetIndexStride(CMDL.IndexFormat format)
        {
            if (format == CMDL.IndexFormat.Uint32) return 4;
            else return 2;
        }
    }
}
