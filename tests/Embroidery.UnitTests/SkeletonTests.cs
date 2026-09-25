using Embroidery.Core.Primitives;
using Embroidery.Geometry;

namespace Embroidery.UnitTests;

public class SkeletonTests
{
    private static Region Poly(params (double X, double Y)[] pts) => new([pts.Select(p => new Vec2(p.X, p.Y)).ToArray()]);

    [Fact]
    public void Long_bar_has_one_centre_branch()
    {
        var branches = RegionSkeleton.Compute(Poly((0, 0), (40, 0), (40, 4), (0, 4)), 0.1);
        var main = Assert.Single(branches);
        Assert.True(main.Length > 30, $"length {main.Length}");
        Assert.All(main.Points, p => Assert.InRange(p.Y, 1.7, 2.3));
        Assert.InRange(main.Radius[main.Radius.Count / 2], 1.8, 2.2);
    }

    [Fact]
    public void T_shape_has_three_branches()
    {
        var t = Poly((0, 0), (40, 0), (40, 4), (22, 4), (22, 30), (18, 30), (18, 4), (0, 4));
        var branches = RegionSkeleton.Compute(t, 0.1);
        Assert.Equal(3, branches.Count);
        Assert.All(branches, b => Assert.True(b.Length > 10, $"branch {b.Length}"));
    }

    [Fact]
    public void Star_has_five_arms()
    {
        var pts = new List<(double, double)>();
        for (var k = 0; k < 10; k++)
        {
            var r = k % 2 == 0 ? 20 : 5;
            var a = Math.PI / 2 + k * Math.PI / 5;
            pts.Add((r * Math.Cos(a), r * Math.Sin(a)));
        }

        var branches = RegionSkeleton.Compute(Poly(pts.ToArray()), 0.1);
        Assert.Equal(5, branches.Count(b => b.StartsAtTip || b.EndsAtTip));
    }

    [Fact]
    public void Ring_skeleton_is_a_closed_loop()
    {
        var outer = Enumerable.Range(0, 72).Select(i => new Vec2(20 * Math.Cos(i * Math.PI / 36), 20 * Math.Sin(i * Math.PI / 36))).ToArray();
        var inner = outer.Select(p => p * 0.8).Reverse().ToArray();
        var branches = RegionSkeleton.Compute(new Region([outer, inner]), 0.1);
        var loop = Assert.Single(branches);
        Assert.False(loop.StartsAtTip || loop.EndsAtTip);
        Assert.InRange(loop.Length, 2 * Math.PI * 18 * 0.95, 2 * Math.PI * 18 * 1.05);
    }
}
