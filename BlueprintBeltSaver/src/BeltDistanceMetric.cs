using System;
using System.Collections.Generic;
namespace BlueprintBeltSaver;

// Blueprint files do not store absolute placement latitude. Use the maximum
// longitudinal scale of each recorded latitude band, not a guessed latitude.
internal sealed class BeltDistanceMetric
{
    private readonly Dictionary<int,double> longitudeScales;
    private readonly double radius;
    internal BeltDistanceMetric(Dictionary<int,double> scales, double planetRadius)
    { longitudeScales=scales; radius=planetRadius; }
    internal double Distance(BlueprintBuilding a,BlueprintBuilding b)
    {
        double longitudeScale;
        if(!longitudeScales.TryGetValue(a.areaIndex,out longitudeScale)) longitudeScale=2*Math.PI/1000;
        double r=radius+0.2+1.3333333*Math.Max(0,Math.Max(a.localOffset_z,b.localOffset_z));
        double x=(b.localOffset_x-a.localOffset_x)*longitudeScale*r;
        double y=(b.localOffset_y-a.localOffset_y)*2*Math.PI/1000*r;
        double z=(b.localOffset_z-a.localOffset_z)*1.3333333;
        // Arc-length upper bound covers grid-line and geodesic paths alike.
        return Math.Sqrt(x*x+y*y+z*z);
    }
}
