using Anichron.Worker.Ingestion.Extraction;
using System.Buffers.Binary;
using System.Text;

namespace Anichron.Worker.Tests.Unit.Ingestion.Extraction;

public sealed class HeifTransformsTests
{
    private static byte[] Box(string type, params byte[][] body)
    {
        var payload = body.SelectMany(part => part).ToArray();
        var box = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        payload.CopyTo(box, 8);
        return box;
    }

    private static byte[] FullBox(string type, byte version, uint flags, params byte[][] body)
        => Box(type, [UInt32(((uint)version << 24) | flags), .. body]);

    private static byte[] UInt16(int value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
        return bytes;
    }

    private static byte[] UInt32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] Irot(byte angle) => Box("irot", [angle]);
    private static byte[] Imir(byte axis) => Box("imir", [axis]);
    private static byte[] Ipco(params byte[][] properties) => Box("ipco", properties);
    private static byte[] Pitm(int item) => FullBox("pitm", 0, 0, UInt16(item));

    // hvcC and ispe stand in front, as in a real file, so the transforms sit at indexes 3 and up.
    private static byte[] Properties(params byte[][] transforms)
        => Ipco([Box("hvcC", new byte[23]), FullBox("ispe", 0, 0, UInt32(64), UInt32(32)), .. transforms]);

    private static byte[] Entry(int item, params byte[] indexes) => [.. UInt16(item), (byte)indexes.Length, .. indexes];

    private static byte[] Ipma(params byte[][] entries) => FullBox("ipma", 0, 0, [UInt32((uint)entries.Length), .. entries]);

    private static byte[] Heif(params byte[][] metaChildren)
        => [.. Box("ftyp", Encoding.ASCII.GetBytes("heic"), new byte[4], Encoding.ASCII.GetBytes("mif1heic")),
            .. FullBox("meta", 0, 0, [FullBox("hdlr", 0, 0, new byte[4], Encoding.ASCII.GetBytes("pict"), new byte[13]), .. metaChildren])];

    private static IReadOnlyList<int> Read(byte[] file) => HeifTransforms.Read(new MemoryStream(file));

    [Fact]
    public void Read_RotationThenMirror_IsBothInThatOrder()
        => Read(Heif(Pitm(1), Box("iprp", Properties(Irot(1), Imir(0)), Ipma(Entry(1, 0x81, 0x82, 0x83, 0x84))))).Should().Equal(8, 4);

    [Fact]
    public void Read_MirrorListedBeforeRotation_IsBothInTheListedOrder()
        => Read(Heif(Pitm(1), Box("iprp", Properties(Irot(1), Imir(0)), Ipma(Entry(1, 0x81, 0x82, 0x84, 0x83))))).Should().Equal(4, 8);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 8)]
    [InlineData(2, 3)]
    [InlineData(3, 6)]
    [InlineData(0xFD, 8)]
    public void Read_Rotation_IsItsAnticlockwiseQuarterTurnsAsAnOrientation(byte angle, int orientation)
        => Read(Heif(Pitm(1), Box("iprp", Properties(Irot(angle)), Ipma(Entry(1, 0x83))))).Should().Equal(orientation);

    [Theory]
    [InlineData(0, 4)]
    [InlineData(1, 2)]
    [InlineData(0xFE, 4)]
    [InlineData(0xFF, 2)]
    public void Read_Mirror_FlipsTopBottomOnAxis0AndLeftRightOnAxis1(byte axis, int orientation)
        => Read(Heif(Pitm(1), Box("iprp", Properties(Imir(axis)), Ipma(Entry(1, 0x83))))).Should().Equal(orientation);

    [Fact]
    public void Read_NonEssentialAssociation_StillCounts()
        => Read(Heif(Pitm(1), Box("iprp", Properties(Irot(1)), Ipma(Entry(1, 0x03))))).Should().Equal(8);

    [Fact]
    public void Read_TransformsOfAnotherItem_AreNotThePrimarys()
        => Read(Heif(Pitm(2), Box("iprp", Properties(Irot(3), Irot(1)), Ipma(Entry(1, 0x83), Entry(2, 0x84))))).Should().Equal(8);

    [Fact]
    public void Read_PrimaryItemAfterTheProperties_IsStillFound()
        => Read(Heif(Box("iprp", Properties(Irot(3), Irot(1)), Ipma(Entry(1, 0x83), Entry(2, 0x84))), Pitm(2))).Should().Equal(8);

    [Fact]
    public void Read_AssociationsBeforeTheirProperties_AreStillResolved()
        => Read(Heif(Pitm(1), Box("iprp", Ipma(Entry(1, 0x83)), Properties(Irot(1))))).Should().Equal(8);

    [Fact]
    public void Read_ThirtyTwoBitItemIds_FindThePrimary()
        => Read(Heif(
                FullBox("pitm", 1, 0, UInt32(70_000)),
                Box("iprp", Properties(Irot(3), Irot(1)),
                    FullBox("ipma", 1, 0, UInt32(2), UInt32(1), [1, 0x83], UInt32(70_000), [1, 0x84]))))
            .Should().Equal(8);

    [Fact]
    public void Read_SixteenBitPropertyIndexes_DropTheEssentialBit()
        => Read(Heif(Pitm(1), Box("iprp", Properties(Irot(1), Imir(1)),
                FullBox("ipma", 0, 1, UInt32(1), UInt16(1), [2], UInt16(0x8003), UInt16(0x0004)))))
            .Should().Equal(8, 2);

    [Fact]
    public void Read_PropertiesThatAreNotTransforms_AreSkipped()
        => Read(Heif(Pitm(1), Box("iprp", Properties(Irot(1)), Ipma(Entry(1, 0x81, 0x82, 0x83))))).Should().Equal(8);

    [Fact]
    public void Read_IndexZeroAndIndexesPastTheProperties_AreSkipped()
        => Read(Heif(Pitm(1), Box("iprp", Properties(Irot(1)), Ipma(Entry(1, 0x80, 0x84, 0x83, 0x7F))))).Should().Equal(8);

    public static TheoryData<string, byte[]> FilesWithoutPrimaryTransforms => new()
    {
        { "no pitm", Heif(Box("iprp", Properties(Irot(1)), Ipma(Entry(1, 0x83)))) },
        { "a primary with no associations", Heif(Pitm(2), Box("iprp", Properties(Irot(1)), Ipma(Entry(1, 0x83)))) },
        { "a primary with no transforms", Heif(Pitm(1), Box("iprp", Properties(), Ipma(Entry(1, 0x81, 0x82)))) },
        { "no ipco", Heif(Pitm(1), Box("iprp", Ipma(Entry(1, 0x83)))) },
        { "properties outside iprp", Heif(Pitm(1), Properties(Irot(1)), Box("iprp", Ipma(Entry(1, 0x83)))) },
        { "properties in another iprp child", Heif(Pitm(1), Box("iprp", Box("free", Properties(Irot(1))), Ipma(Entry(1, 0x83)))) },
        { "associations outside iprp", Heif(Pitm(1), Box("iprp", Properties(Irot(1))), Ipma(Entry(1, 0x83))) },
        { "no meta", [.. Box("ftyp", Encoding.ASCII.GetBytes("heic")), .. Box("free", Pitm(1), Box("iprp", Properties(Irot(1)), Ipma(Entry(1, 0x83))))] },
        { "an empty file", [] },
    };

    [Theory]
    [MemberData(nameof(FilesWithoutPrimaryTransforms))]
    public void Read_FileWithoutTransformsOnItsPrimaryItem_IsNone(string _, byte[] file)
        => Read(file).Should().BeEmpty();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(20)]
    public void Read_TruncatedFile_FindsNothing(int cut)
        => Read(Heif(Pitm(1), Box("iprp", Properties(Irot(1)), Ipma(Entry(1, 0x83))))[..^cut]).Should().BeEmpty();
}
