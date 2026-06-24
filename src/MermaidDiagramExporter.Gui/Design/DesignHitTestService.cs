using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace MermaidDiagramExporter.Gui.Design;

/// <summary>
/// Result of a sub-region hit-test on a class rectangle. Carries the
/// rectangle and the hit kind so the controller knows what to do.
/// </summary>
public readonly record struct DesignHitResult(ClassRectangle? Rectangle, ClassRectangleHitTest Kind, int MemberIndex = -1)
{
    public static DesignHitResult None() => new(null, ClassRectangleHitTest.None);

    public static DesignHitResult Body(ClassRectangle rect) => new(rect, ClassRectangleHitTest.Body);
    public static DesignHitResult Header(ClassRectangle rect) => new(rect, ClassRectangleHitTest.Header);
    public static DesignHitResult Member(ClassRectangle rect, int index) => new(rect, ClassRectangleHitTest.Member, index);
    public static DesignHitResult ResizeHandle(ClassRectangle rect) => new(rect, ClassRectangleHitTest.ResizeHandle);
    public static DesignHitResult LeftPort(ClassRectangle rect) => new(rect, ClassRectangleHitTest.LeftPort);
    public static DesignHitResult RightPort(ClassRectangle rect) => new(rect, ClassRectangleHitTest.RightPort);
}

/// <summary>
/// Sub-region hit-testing for Design Mode. <c>HitTestService.HitTest</c> only
/// finds the topmost node; this service distinguishes header vs. body vs.
/// resize handle vs. edge ports. O(n) linear scan over rectangles — same
/// performance characteristics as <c>HitTestService</c>. Acceptable at 50–100
/// classes; spatial pruning is a future optimization per docs/design/08.
/// </summary>
public static class DesignHitTestService
{
    /// <summary>
    /// Finds the topmost class rectangle hit by a world-space point, and the
    /// specific sub-region within it.
    /// </summary>
    public static DesignHitResult HitTest(SKPoint worldPos, IReadOnlyList<ClassRectangle> rectangles)
    {
        // Reverse iterate so the topmost (last-drawn) rectangle wins
        for (int i = rectangles.Count - 1; i >= 0; i--)
        {
            var rect = rectangles[i];
            var kind = rect.HitTest(worldPos);
            if (kind == ClassRectangleHitTest.None) continue;

            // If it's a member hit, compute which member index
            int memberIndex = -1;
            if (kind == ClassRectangleHitTest.Body)
            {
                // Body hit — figure out if it's on a member row
                var cls = FindClass(rect, rectangles);
                if (cls != null)
                {
                    float relativeY = worldPos.Y - rect.Y - DesignGeometry.HeaderHeight;
                    if (relativeY >= 0)
                    {
                        int idx = (int)(relativeY / DesignGeometry.MemberRowHeight);
                        if (idx >= 0 && idx < cls.Members.Count)
                        {
                            memberIndex = idx;
                            kind = ClassRectangleHitTest.Member;
                        }
                    }
                }
            }

            return kind switch
            {
                ClassRectangleHitTest.ResizeHandle => DesignHitResult.ResizeHandle(rect),
                ClassRectangleHitTest.LeftPort => DesignHitResult.LeftPort(rect),
                ClassRectangleHitTest.RightPort => DesignHitResult.RightPort(rect),
                ClassRectangleHitTest.Header => DesignHitResult.Header(rect),
                ClassRectangleHitTest.Member => DesignHitResult.Member(rect, memberIndex),
                _ => DesignHitResult.Body(rect)
            };
        }

        return DesignHitResult.None();
    }

    private static DesignClass? FindClass(ClassRectangle rect, IReadOnlyList<ClassRectangle> rectangles)
    {
        // The rectangle's ClassId maps to a DesignClass in its owning Graph.
        // For now, return the first class with matching Id (O(n) but fine for small designs).
        var cls = rect.Graph.Classes.FirstOrDefault(c => c.Id == rect.ClassId);
        return cls;
    }
}
