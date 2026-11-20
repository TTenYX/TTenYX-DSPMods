using System.Reflection;
using System.Globalization;
using System.Text;
using UnityEngine;
const BindingFlags BF=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
var root="X:/戴森球计划/【MOD】修改仓库/1.我的MOD/BlueprintBeltSaver";
var managed=root+"/tools/restorecheck/bin/Release/net8.0";
AppDomain.CurrentDomain.AssemblyResolve+=(s,a)=>{var p=Path.Combine(managed,new AssemblyName(a.Name).Name+".dll");return File.Exists(p)?Assembly.LoadFrom(p):null;};
var log=new StringBuilder();void Out(string s){log.AppendLine(s);Console.WriteLine(s);}
var source=args.Length>0?args[0]:root+"/docs/handoff/data/restore-case001-source.txt";
if(!File.Exists(source)){Console.Error.WriteLine("Missing source: "+source);return 2;}
var bytes=File.ReadAllBytes(source);var bp=new BlueprintData();var error=bp.FromBase64String(Encoding.UTF8.GetString(bytes).Trim());
Out("GAME_PARSE "+error+" SHA256="+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
if(error!=BlueprintDataIOError.OK)return 2;
foreach(var n in bp.buildings)Out($"NODE {n.index} item={n.itemId} model={n.modelIndex} area={n.areaIndex} xyz=({n.localOffset_x:R},{n.localOffset_y:R},{n.localOffset_z:R}) xyz2=({n.localOffset_x2:R},{n.localOffset_y2:R},{n.localOffset_z2:R}) in={n.inputObj?.index??-1} out={n.outputObj?.index??-1} slots=({n.outputToSlot},{n.inputFromSlot},{n.outputFromSlot},{n.inputToSlot},{n.outputOffset},{n.inputOffset}) metadata=({n.filterId},{n.recipeId},{n.parameters?.Length??0},{n.content??"null"})");
var dll=args.Length>1?args[1]:root+"/tools/restorecheck/bin/Release/net8.0/BlueprintBeltSaver.dll";
if(!File.Exists(dll)){Console.Error.WriteLine("Missing baseline DLL: "+dll);return 2;}
var baselineHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(dll)));
if(baselineHash!="681B1063F0410D4401F2830973A102DFE4D52A23D0B23DFAEC99F1F00D123D0C")
{Console.Error.WriteLine("Historical diagnostic requires the 681B1063 baseline DLL as argument 2. Use RestoreCheck --pose-dump for the current engine.");return 2;}
var mod=Assembly.LoadFrom(dll);
var rest=mod.GetType("BlueprintBeltSaver.BlueprintGridRestorer");var envType=mod.GetType("BlueprintBeltSaver.PasteEnvelope");
Out("MOD_SHA256="+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(mod.Location))));
object Call(Type t,string name,params object[] a)=>t.GetMethod(name,BF).Invoke(null,a);
object Value(object o,string n)=>o.GetType().GetField(n,BF)?.GetValue(o)??o.GetType().GetProperty(n,BF)?.GetValue(o);
Func<BlueprintBuilding,bool> belt=n=>n!=null&&n.itemId>=2001&&n.itemId<=2009;
var env=Call(envType,"Build",bp.areas,bp.primaryAreaIdx,bp.buildings,belt,true);
var path=new List<BlueprintBuilding>();for(var n=bp.buildings[12];n!=null;n=n.outputObj)path.Add(n);
Out("PATH="+string.Join(",",path.Select(n=>n.index))+" ENVELOPE="+env);
for(int i=1;i<path.Count;i++)Out($"AXIS {path[i-1].index}->{path[i].index}="+Call(rest,"LineAxis",path[i-1],path[i]));
var rep=(List<BlueprintBuilding>)Call(rest,"GridSequence",path,0,env,true);
Out("SEQUENCE="+(rep==null?"NULL":string.Join(";",rep.Select(n=>$"{n.index}:({n.localOffset_x:R},{n.localOffset_y:R},{n.localOffset_z:R}) old={path.Contains(n)}"))));
if(rep!=null)foreach(var n in rep){bool native=(bool)Call(rest,"NativeGridScaleMatches",n,env);Out($"NATIVE {n.index} x={n.localOffset_x:R} y={n.localOffset_y:R} =>{native}");if(!native)foreach(var f in (System.Collections.IEnumerable)Value(env,"ProtectedFrames")){var latArray=(float[])Value(f,"RefLat");var widths=(float[])Value(f,"LonPerGrid");bool swap=(bool)Value(f,"Swap");float sign=(float)Value(f,"LatSign");float lat=latArray[0]+(swap?n.localOffset_x:n.localOffset_y)*BlueprintUtils.GetLatitudeRadPerGrid(200)*sign;float actual=BlueprintUtils.GetLongitudeRadPerGrid(lat,200);if(Math.Abs(actual-widths[0])>0.00000005f)Out($"  MISMATCH {f} swap={swap} sign={sign:R} refLat={latArray[0]:R} lat={lat:R} referenceWidth={widths[0]:R} nativeWidth={actual:R}");}}
var frames=((System.Collections.IEnumerable)Value(env,"ProtectedFrames")).Cast<object>().ToArray();
Vector3 Pos(BlueprintBuilding n,object f)=>(Vector3)Call(envType,"LPos",n,f);
var badFrames=new List<object>();
foreach(var f in frames){var spans=path.Skip(1).Select((n,i)=>(a:path[i],b:n,d:(Pos(n,f)-Pos(path[i],f)).magnitude)).ToArray();var min=spans.MinBy(s=>s.d);if(min.d<0.400001){badFrames.Add(f);Out($"SOURCE_TOO_SHORT {f} min={min.d:R} edge={min.a.index}->{min.b.index} max={spans.Max(s=>s.d):R}");}}
Out("SOURCE_MIN_BAD_POSES="+badFrames.Count+" PROTECTED="+frames.Length);
foreach(var anchors in new[]{new List<BlueprintBuilding>{bp.buildings[12],bp.buildings[11]},path.Skip(1).ToList()}){
 var seq=(List<BlueprintBuilding>)Call(rest,"GridSequence",anchors,0,env,true);Out("ACTUAL_ANCHOR_SEQUENCE "+string.Join(",",anchors.Select(n=>n.index))+" => "+(seq==null?"NULL":string.Join(",",seq.Select(n=>n.localOffset_x.ToString("R")))));
}
var gs=new List<BlueprintBuilding>{path[0],path.Last()};gs.AddRange(path.Skip(1).Take(path.Count-2).Where(n=>Math.Abs(n.localOffset_x-Math.Round(n.localOffset_x))<=.0002));for(int x=0;x<=13;x++){if(x-path[0].localOffset_x<=.0002||path.Last().localOffset_x-x<=.0002||gs.Any(n=>Math.Abs(n.localOffset_x-x)<=.0002))continue;gs.Add(new BlueprintBuilding{index=-100-x,areaIndex=0,itemId=2001,modelIndex=35,localOffset_x=x,localOffset_y=0,localOffset_z=0});}gs.Sort((a,b)=>a.localOffset_x.CompareTo(b.localOffset_x));
for(int i=gs.Count-2;i>=1;i--){var n=gs[i];if(path.Contains(n))continue;foreach(var f in frames){float a=(Pos(n,f)-Pos(gs[i-1],f)).magnitude,b=(Pos(gs[i+1],f)-Pos(n,f)).magnitude;if(a<.400001||b<.400001){Out($"REJECT_NEW x={n.localOffset_x} pose={f} left={gs[i-1].localOffset_x:R} right={gs[i+1].localOffset_x:R} distances={a:R},{b:R}");gs.RemoveAt(i);break;}}}
Out("AFTER_MIN_PRUNE="+string.Join(",",gs.Select(n=>n.localOffset_x.ToString("R"))));
for(int i=1;i<gs.Count;i++){var a=gs[i-1];var b=gs[i];bool old=path.Any(n=>ReferenceEquals(n,a)&&ReferenceEquals(n.outputObj,b));if(old)continue;double distance=(double)envType.GetMethod("Distance",BF).Invoke(env,new object[]{a,b});Out($"POST_PRUNE_NEW_SPAN {a.localOffset_x:R}->{b.localOffset_x:R} max={distance:R} threshold={2.301172*.98:R}");}
foreach(bool height in new[]{true,false}){object[] a={bp,null,null,height,false};bool ok=(bool)Call(rest,"TryRestore",a);Out($"RESTORE height={height} ok={ok} "+string.Join(" ",new[]{"Before","After","Added","Removed","RestoredRuns","ProtectedRuns","ProtectedNodes","Reason"}.Select(k=>k+"="+Value(a[2],k))));}
var normalized=new BlueprintData();normalized.FromBase64String(Encoding.UTF8.GetString(bytes).Trim());foreach(var n in normalized.buildings){if(Math.Abs(n.localOffset_x-Math.Round(n.localOffset_x))<=0.0002)n.localOffset_x=n.localOffset_x2=(float)Math.Round(n.localOffset_x);if(Math.Abs(n.localOffset_y-Math.Round(n.localOffset_y))<=0.0002)n.localOffset_y=n.localOffset_y2=(float)Math.Round(n.localOffset_y);if(Math.Abs(n.localOffset_z-Math.Round(n.localOffset_z*2)/2)<=0.0002)n.localOffset_z=n.localOffset_z2=(float)(Math.Round(n.localOffset_z*2)/2);}
object[] snapped={normalized,null,null,true,false};var snapOk=(bool)Call(rest,"TryRestore",snapped);Out($"NOISE_NORMALIZED_RESTORE ok={snapOk} "+string.Join(" ",new[]{"Before","After","Added","Removed","RestoredRuns","ProtectedRuns","ProtectedNodes","Reason"}.Select(k=>k+"="+Value(snapped[2],k))));
var mutableFrames=(System.Collections.IList)Value(env,"ProtectedFrames");foreach(var f in badFrames)mutableFrames.Remove(f);
Out("DIAGNOSTIC_FILTERED_FRAMES="+mutableFrames.Count+" (groups remain conservative original superset)");
var remaining=frames.Except(badFrames).ToArray();var sourceDistances=remaining.SelectMany(f=>path.Skip(1).Select((n,i)=>(Pos(n,f)-Pos(path[i],f)).magnitude)).ToArray();Out($"FILTERED_SOURCE_DISTANCES min={sourceDistances.Min():R} max={sourceDistances.Max():R}");
var filtered=(List<BlueprintBuilding>)Call(rest,"GridSequence",path.Skip(1).ToList(),0,env,true);Out("FILTERED_SEQUENCE="+(filtered==null?"NULL":string.Join(";",filtered.Select(n=>$"{n.index}:({n.localOffset_x:R},{n.localOffset_y:R},{n.localOffset_z:R}) old={path.Contains(n)} native={Call(rest,"NativeGridScaleMatches",n,env)}"))));
if(filtered!=null){float min=999;double max=0;foreach(var f in frames.Except(badFrames))for(int i=1;i<filtered.Count;i++){var d=(Pos(filtered[i],f)-Pos(filtered[i-1],f)).magnitude;min=Math.Min(min,d);max=Math.Max(max,d);}Out($"FILTERED_SEQUENCE_EXACT_DISTANCES min={min:R} max={max:R}");}
var filteredNorm=(List<BlueprintBuilding>)Call(rest,"GridSequence",normalized.buildings.Skip(0).Reverse().Skip(1).ToList(),0,env,true);Out("FILTERED_NORMALIZED_SEQUENCE="+(filteredNorm==null?"NULL":string.Join(";",filteredNorm.Select(n=>$"{n.index}:({n.localOffset_x:R},{n.localOffset_y:R},{n.localOffset_z:R}) native={Call(rest,"NativeGridScaleMatches",n,env)}"))));
if(!bytes.SequenceEqual(File.ReadAllBytes(source)))throw new Exception("source changed");
File.WriteAllText(root+"/tools/case001-independent-analysis/game-analysis.txt",log.ToString(),new UTF8Encoding(false));
return 0;
