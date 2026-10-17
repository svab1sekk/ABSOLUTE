using System.Buffers.Binary;
using System.Text;
using Absolute.Core;
using Xunit;

namespace Absolute.Core.Tests;

public sealed class HardwareIdentityTests
{
    [Fact]
    public void MemoryKitsAreCombinedWithoutSlotNames()
    {
        var module=new MemoryModuleInfo("Unknown","PVV532G600C36K",16UL*1024*1024*1024,34,6000);
        var part=HardwareIdentity.CombineMemory([module,module],(ulong)(31.8*1073741824));
        Assert.Equal("Patriot Viper Venom · 32 GB DDR5",part.Name);
        Assert.Contains(part.Details,d=>d.Key=="Usable"&&d.Value=="31.8 GB");
        Assert.Contains(part.Details,d=>d.Key=="Speed"&&d.Value=="6000 MT/s");
    }

    [Fact]
    public void MixedMemoryDoesNotClaimOneBrandOrSpeed()
    {
        var part=HardwareIdentity.CombineMemory([new("Corsair","CMK16",16UL<<30,26,3200),new("Kingston","KF16",8UL<<30,26,2666)],24UL<<30);
        Assert.Equal("24 GB DDR4",part.Name);Assert.DoesNotContain(part.Details,d=>d.Key=="Speed");
    }

    [Fact]
    public void EdidIdentifiesTheMonitorAndNativeMode()
    {
        var bytes=Edid("P25S Plus");var monitor=HardwareIdentity.ReadEdid(bytes);
        Assert.NotNull(monitor);Assert.Equal("TITAN ARMY P25S Plus",monitor.Name);
        Assert.Equal(1920,monitor.Width);Assert.Equal(1080,monitor.Height);Assert.InRange(monitor.RefreshRate,59.9,60.1);
        bytes[20]^=1;Assert.Null(HardwareIdentity.ReadEdid(bytes));
    }

    [Theory]
    [InlineData("HID-compliant mouse",false)]
    [InlineData("Generic PnP Monitor",false)]
    [InlineData("USB Input Device",false)]
    [InlineData("Wireless Keyboard",false)]
    [InlineData("Logitech USB Receiver",false)]
    [InlineData("Realtek High Definition Audio",false)]
    [InlineData("NVIDIA High Definition Audio",false)]
    [InlineData("Logitech G PRO X SUPERLIGHT 2",true)]
    [InlineData("Wooting 80HE",true)]
    [InlineData("HyperX QuadCast",true)]
    [InlineData("USB Dell KB216",true)]
    [InlineData("HyperVMonitor",false)]
    public void OnlySpecificPhysicalProductNamesAreShown(string name,bool specific)=>Assert.Equal(specific,HardwareIdentity.IsSpecificPeripheral(name));

    [Fact]
    public void CompositeGamingDevicesKeepTheirPhysicalProductCategory()
    {
        Assert.Equal("Mouse",HardwareIdentity.PeripheralCategory("Logitech PRO X SUPERLIGHT 2","Keyboard"));
        Assert.Equal("Keyboard",HardwareIdentity.PeripheralCategory("Wooting 80HE","Mouse"));
    }

    [Fact]
    public void UnknownEdidNamesStayUnknownInsteadOfBecomingABrand()
    {
        Assert.Equal("",HardwareIdentity.MonitorName("","DEL"));
        Assert.Equal("",HardwareIdentity.MonitorName("Generic PnP Monitor","DEL"));
        Assert.Equal("Dell U2723QE",HardwareIdentity.MonitorName("U2723QE","DEL"));
    }

    [Fact]
    public void MotherboardPackagesExcludeAudioAndVirtualEndpoints()
    {
        DriverDevice Device(string id,string name,string type)=>new(id,name,type,"","","","",[],true);
        Assert.True(HardwareIdentity.IsMotherboardDriver(Device(@"PCI\VEN_1022&DEV_43F7","AMD PCI Express","SYSTEM")));
        Assert.True(HardwareIdentity.IsMotherboardDriver(Device(@"USB\VID_8087&PID_0033","Intel Bluetooth","Bluetooth")));
        Assert.False(HardwareIdentity.IsMotherboardDriver(Device(@"PCI\VEN_10EC","Realtek Audio","MEDIA")));
        Assert.False(HardwareIdentity.IsMotherboardDriver(Device(@"ROOT\NET\0000","Virtual adapter","NET")));
    }

    [Fact]
    public void TemperatureSelectionUsesPackageAndGpuCoreInsteadOfDistanceOrJunction()
    {
        SensorReading[] values=[new("CPU Core #1","/intelcpu/0","Temperature",45),new("Distance to TjMax","/intelcpu/0","Temperature",90),new("CPU Package","/intelcpu/0","Temperature",50),new("GPU Core","/gpu-nvidia/0","Temperature",65),new("GPU Hot Spot","/gpu-nvidia/0","Temperature",85)];
        Assert.Equal(50,SensorTemperatures.Read(values,"cpu"));Assert.Equal(65,SensorTemperatures.Read(values,"gpu"));
        Assert.Null(SensorTemperatures.Read([new("CPU Package","/intelcpu/0","Temperature",float.NaN)],"cpu"));
    }

    private static byte[] Edid(string name)
    {
        var bytes=new byte[128];new byte[]{0,255,255,255,255,255,255,0}.CopyTo(bytes,0);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(8),(ushort)((20<<10)|(9<<5)|20));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10),0x1234);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(54),14850);
        bytes[56]=128;bytes[57]=24;bytes[58]=0x71;bytes[59]=56;bytes[60]=45;bytes[61]=0x40;
        bytes[75]=0xFC;Encoding.ASCII.GetBytes(name).CopyTo(bytes,77);
        bytes[127]=(byte)((256-bytes.Take(127).Sum(b=>(int)b)%256)%256);return bytes;
    }
}
