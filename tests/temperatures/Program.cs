using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CodexAccountDesk;

var checks=new List<string>(); var now=DateTimeOffset.UtcNow;
void Assert(bool value) { if(!value) throw new InvalidOperationException("Temperature contract assertion failed."); }
void Check(string name,Action action) { action(); checks.Add(name); }
byte[] Descriptor(params (ushort Index,short Value)[] sensors)
{
    var bytes=new byte[24+sensors.Length*16]; BinaryPrimitives.WriteUInt32LittleEndian(bytes,40); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4),(uint)bytes.Length);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12),(ushort)sensors.Length);
    for(var i=0;i<sensors.Length;i++) { BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(24+i*16),sensors[i].Index); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26+i*16),sensors[i].Value); }
    return bytes;
}
TemperatureReading Parse(byte[] bytes)=>WindowsDiskTemperature.ParseDescriptor(bytes,"fixture storage sensor",now);
Check("Zero and negative Celsius are real values; NaN/infinity/out-of-range are rejected",()=>{
    foreach(var value in new[]{0.0,-20.0,200.0}) Assert(TemperatureReading.From(value,"fixture","",now).State==TemperatureState.Available);
    foreach(var value in new[]{double.NaN,double.PositiveInfinity,-100.0,32767.0}) Assert(TemperatureReading.From(value,"fixture","",now).State==TemperatureState.InvalidData);
});
Check("Current storage composite sensor takes precedence over hotter secondary sensors",()=>{ Assert(Parse(Descriptor((0,49),(1,43),(2,58))).Celsius==49); });
Check("Storage without a composite reports the highest available actual sensor",()=>{ Assert(Parse(Descriptor((1,43),(2,58))).Celsius==58); });
Check("The documented unreported sentinel never appears as Celsius",()=>{ Assert(Parse(Descriptor((0,short.MinValue))).State==TemperatureState.SensorUnavailable); Assert(Parse(Descriptor((0,short.MinValue),(2,58))).Celsius==58); });
Check("Truncated storage responses and inconsistent sizes are rejected",()=>{
    var good=Descriptor((0,50)); Assert(Parse(good[..^1]).State==TemperatureState.InvalidData); Assert(Parse(new byte[10]).State==TemperatureState.InvalidData);
    BinaryPrimitives.WriteUInt32LittleEndian(good.AsSpan(4),4000); Assert(Parse(good).State==TemperatureState.InvalidData);
});
Check("Storage invalid versions/counts/temperatures and duplicate sensor identities are rejected",()=>{
    var bytes=Descriptor((0,50)); BinaryPrimitives.WriteUInt32LittleEndian(bytes,0); Assert(Parse(bytes).State==TemperatureState.InvalidData);
    bytes=Descriptor((0,50)); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12),129); Assert(Parse(bytes).State==TemperatureState.InvalidData);
    Assert(Parse(Descriptor((0,32767))).State==TemperatureState.InvalidData); Assert(Parse(Descriptor((1,40),(1,50))).State==TemperatureState.InvalidData);
});
Check("Volume extents map actual non-sequential disks and deduplicate partitions",()=>{
    var bytes=new byte[80]; BinaryPrimitives.WriteUInt32LittleEndian(bytes,3);
    foreach(var pair in new[]{(8,9u),(32,3u),(56,9u)}) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(pair.Item1),pair.Item2);
    Assert(WindowsDiskTemperature.ParseExtents(bytes).SequenceEqual(new[]{3u,9u}));
});
Check("Malformed/truncated/unbounded volume extents are rejected",()=>{
    foreach(var bytes in new[]{new byte[7],new byte[8],new byte[]{255,255,255,255,0,0,0,0}})
    { var rejected=false;try {WindowsDiskTemperature.ParseExtents(bytes);}catch(FormatException){rejected=true;}Assert(rejected); }
});
Check("Multi-disk volumes report highest available reading with honest partial coverage",()=>{
    var reading=WindowsDiskTemperature.Aggregate(new[]{TemperatureReading.From(45,"disk 7","",now),TemperatureReading.Missing(TemperatureState.AccessDenied,"disk 9",at:now)});
    Assert(reading.Celsius==45&&reading.Source.Contains("1/2")&&reading.Detail.Contains("AccessDenied")&&reading.Scope==TemperatureScope.BackingDisks);
    reading=WindowsDiskTemperature.Aggregate(new[]{TemperatureReading.From(45,"disk 7","",now),TemperatureReading.From(61,"disk 9","",now)}); Assert(reading.Celsius==61&&reading.Source.Contains("2/2"));
});
Check("All-unavailable disks preserve access-denied state and invalid roots do not query devices",()=>{
    Assert(WindowsDiskTemperature.Aggregate(new[]{TemperatureReading.Missing(TemperatureState.SensorUnavailable),TemperatureReading.Missing(TemperatureState.AccessDenied)}).State==TemperatureState.AccessDenied);
    Assert(WindowsDiskTemperature.Read("../").State==TemperatureState.InvalidData);
});
var sensors=new[]{new TemperatureSensor("cpu0","CPU","Cpu","CPU Package",63),new TemperatureSensor("board","Motherboard","SuperIO","CPU Socket",99),
    new TemperatureSensor("ram0","DIMM 0","Memory","DIMM",42),new TemperatureSensor("ram1","DIMM 1","Memory","DIMM",48),
    new TemperatureSensor("gpu0","Example GPU","GpuNvidia","GPU Core",53),new TemperatureSensor("gpu0","Example GPU","GpuNvidia","GPU Memory Junction",74)};
var adapter=new GpuResource("00000000:00000001","Example GPU",1,0,0);
Check("CPU and RAM temperatures use their actual hardware types, never motherboard socket substitutes",()=>{
    Assert(TemperatureSensors.Resource(sensors,"Cpu","fixture WMI",now).Celsius==63); Assert(TemperatureSensors.Resource(sensors,"Memory","fixture WMI",now).Celsius==48);
});
Check("GPU core and VRAM sensor channels remain independent",()=>{
    Assert(TemperatureSensors.Gpu(sensors,new[]{adapter},adapter,false,"fixture WMI",now).Celsius==53);
    Assert(TemperatureSensors.Gpu(sensors,new[]{adapter},adapter,true,"fixture WMI",now).Celsius==74);
    Assert(TemperatureSensors.Gpu(sensors.Where(s=>s.Name!="GPU Memory Junction"),new[]{adapter},adapter,true,"fixture WMI",now).Celsius==null);
});
Check("Duplicate GPU names or provider hardware identities are ambiguous and never guessed",()=>{
    Assert(TemperatureSensors.Gpu(sensors,new[]{adapter,adapter with {Id="00000000:00000002"}},adapter,false,"fixture WMI",now).Celsius==null);
    Assert(TemperatureSensors.Gpu(sensors.Append(new("gpu1","Example GPU","GpuNvidia","GPU Core",80)),new[]{adapter},adapter,false,"fixture WMI",now).Celsius==null);
});
Check("Invalid provider readings are reported rather than silently replacing real sensor data",()=>{
    Assert(TemperatureSensors.Resource(sensors.Append(new("cpu1","CPU 1","Cpu","Core",double.NaN)),"Cpu","fixture WMI",now).State==TemperatureState.InvalidData);
});
Check("Stale/future/unavailable values display an unavailable marker, not retained fake values",()=>{
    Assert(TemperaturePresentation.Label(TemperatureReading.From(53,"fixture","",now.AddMinutes(-1)),now)=="—°C");
    Assert(TemperaturePresentation.Label(TemperatureReading.From(53,"fixture","",now.AddMinutes(1)),now)=="—°C");
    Assert(TemperaturePresentation.Label(null,now)=="—°C"); Assert(TemperaturePresentation.Label(TemperatureReading.From(0,"fixture","",now),now)=="0°C");
});
foreach(var info in Loc.Languages)
    Check(info.Code+" temperature labels/statuses preserve local culture and all messages",()=>{
        Loc.SetLanguage(info.Code);Assert(TemperaturePresentation.Label(TemperatureReading.From(53.4,"fixture","",now),now)=="53°C");
        Assert(TemperaturePresentation.Tooltip(TemperatureReading.From(53.4,"fixture","",now),now).Contains(53.4.ToString("0.#",Loc.Culture)));
        var aggregate=WindowsDiskTemperature.Aggregate(new[]{TemperatureReading.From(45,"disk 7","",now),TemperatureReading.From(61,"disk 9","",now)});
        Assert(TemperaturePresentation.Tooltip(aggregate,now).Contains(Loc.T("thermal.backing_disks")));
        foreach(var state in Enum.GetValues<TemperatureState>().Where(s=>s!=TemperatureState.Available)) Assert(!string.IsNullOrWhiteSpace(TemperaturePresentation.Tooltip(TemperatureReading.Missing(state),now)));
        Assert(TemperatureColors.Tooltip(TemperatureReading.From(53.4,"fixture","",now),TemperatureKind.Gpu).Contains(Loc.T("thermal.default_limits")));
    });
Check("Temperature colors classify caution/high independently of utilization",()=>{
    Assert(TemperatureColors.Pressure(TemperatureReading.From(77,"fixture","",now),TemperatureKind.Cpu,ResourcePressure.Normal,now)==ResourcePressure.Warning);
    Assert(TemperatureColors.Pressure(TemperatureReading.From(92,"fixture","",now),TemperatureKind.Gpu,ResourcePressure.Normal,now)==ResourcePressure.Critical);
    Assert(TemperatureColors.Pressure(null,TemperatureKind.Gpu,ResourcePressure.Critical,now)==ResourcePressure.Unknown);
    Assert(TemperatureColors.Color(ResourcePressure.Normal)=="text"&&TemperatureColors.Color(ResourcePressure.Warning)=="warning"&&TemperatureColors.Color(ResourcePressure.Critical)=="danger"&&TemperatureColors.Color(ResourcePressure.Unknown)=="muted");
});
Check("Valid reported limits override display defaults without inventing device limits",()=>{
    var reading=TemperatureReading.From(71,"fixture disk","",now) with {Limits=TemperatureLimits.Device(80,90)};
    Assert(TemperatureColors.Pressure(reading,TemperatureKind.Disk,ResourcePressure.Normal,now)==ResourcePressure.Normal);
    Assert(TemperatureLimits.Device(90,80)==null&&TemperatureLimits.Device(0,90)==null&&TemperatureLimits.Device(double.NaN,90)==null);
});
Check("Storage warning/critical thresholds preserve native Celsius units",()=>{
    var bytes=Descriptor((0,71));BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(8),90);BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(10),80);
    var reading=Parse(bytes);Assert(reading.Limits is {Warning:80,Critical:90,Reported:true});
});
Check("Temperature warning colors release three Celsius below thresholds",()=>{
    var reading=TemperatureReading.From(73,"fixture CPU","",now);
    Assert(TemperatureColors.Pressure(reading,TemperatureKind.Cpu,ResourcePressure.Warning,now)==ResourcePressure.Warning);
    Assert(TemperatureColors.Pressure(reading with {Celsius=71},TemperatureKind.Cpu,ResourcePressure.Warning,now)==ResourcePressure.Normal);
});
Check("Multi-device color reflects the worst actual device relative to its own limits",()=>{
    var aggregate=WindowsDiskTemperature.Aggregate(new[]{TemperatureReading.From(71,"disk 1","",now) with {Limits=TemperatureLimits.Device(80,90)},TemperatureReading.From(65,"disk 2","",now) with {Limits=TemperatureLimits.Device(55,60)}});
    Assert(aggregate.Celsius==71&&TemperatureColors.Pressure(aggregate,TemperatureKind.Disk,ResourcePressure.Normal,now)==ResourcePressure.Critical);
});
Loc.SetLanguage("en");
Console.WriteLine(JsonSerializer.Serialize(new {passed=checks.Count,failed=0,checks,scope="Production storage parsing, identity/channel selection and presentation contracts; synthetic bytes/metadata only, no native device queries, accounts or process actions."},new JsonSerializerOptions {WriteIndented=true}));
