using Avalonia.Media;

namespace BackupZCrypt.Desktop;

/// <summary>
/// Monochrome vector icons for the application, hand-built on a 24x24 grid in the Fluent "filled" style.
/// Every geometry relies on Avalonia's default even-odd fill rule: inner sub-paths cut holes out of outer
/// silhouettes and sub-paths never partially overlap, so the shapes render identically on all platforms.
/// Stroke-like details keep a consistent visual weight of roughly 2px. Intended to be consumed by
/// <see cref="Avalonia.Controls.PathIcon"/> through <c>{x:Static}</c> (fill only, no stroke).
/// </summary>
internal static class Icons
{
    /// <summary>
    /// Gets the create-backup icon: a shield silhouette (straight shoulders, Bezier-tapered tip) with a
    /// padlock cut out of it, built from a half-ring shackle over a rounded-square body.
    /// </summary>
    public static StreamGeometry ShieldLock { get; } = StreamGeometry.Parse(
        "M12,2.2 L19.5,4.8 V10.5 C19.5,15.3 16.6,19.2 12,21.6 C7.4,19.2 4.5,15.3 4.5,10.5 V4.8 Z " +
        "M8.8,10.5 V9.6 A3.2,3.2 0 0 1 15.2,9.6 V10.5 H13.7 V9.6 A1.7,1.7 0 0 0 10.3,9.6 V10.5 Z " +
        "M9.5,10.5 H14.5 A1.5,1.5 0 0 1 16,12 V14.5 A1.5,1.5 0 0 1 14.5,16 H9.5 A1.5,1.5 0 0 1 8,14.5 V12 " +
        "A1.5,1.5 0 0 1 9.5,10.5 Z");

    /// <summary>
    /// Gets the update-backup icon: two 120-degree annular arc segments (outer radius 9, inner radius 7) of a
    /// circle centered at (12,12), each ending in a solid triangular arrowhead that shares the arc's radial end edge.
    /// </summary>
    public static StreamGeometry ArrowSync { get; } = StreamGeometry.Parse(
        "M4.21,7.5 A9,9 0 0 1 19.79,7.5 L18.06,8.5 A7,7 0 0 0 5.94,8.5 Z " +
        "M20.92,6.85 L20.63,10.94 L16.94,9.15 Z " +
        "M19.79,16.5 A9,9 0 0 1 4.21,16.5 L5.94,15.5 A7,7 0 0 0 18.06,15.5 Z " +
        "M3.08,17.15 L3.37,13.06 L7.06,14.85 Z");

    /// <summary>
    /// Gets the restore-backup icon: a solid downward arrow (bar and chevron head drawn as one polygon) dropping
    /// into an open U-shaped tray with 2px walls and rounded outer bottom corners.
    /// </summary>
    public static StreamGeometry BoxArrowDown { get; } = StreamGeometry.Parse(
        "M10.9,2.5 H13.1 V9.5 H16.8 L12,15 L7.2,9.5 H10.9 Z " +
        "M4,13 V19 A2,2 0 0 0 6,21 H18 A2,2 0 0 0 20,19 V13 H18 V19 H6 V13 Z");

    /// <summary>
    /// Gets the verify-backup icon: the same shield silhouette as <see cref="ShieldLock"/> with a mitred 2px
    /// checkmark polygon cut out of its center.
    /// </summary>
    public static StreamGeometry ShieldCheck { get; } = StreamGeometry.Parse(
        "M12,2.2 L19.5,4.8 V10.5 C19.5,15.3 16.6,19.2 12,21.6 C7.4,19.2 4.5,15.3 4.5,10.5 V4.8 Z " +
        "M7.4,11.7 L8.8,10.3 L10.8,12.3 L15.2,7.9 L16.6,9.3 L10.8,15.1 Z");

    /// <summary>
    /// Gets the settings icon: an eight-tooth gear built as a 32-vertex polygon alternating between tip radius 10
    /// and root radius 7.8 (tip half-angle 8 degrees, root half-angle 14.5 degrees), with a radius-4.2 center hole.
    /// </summary>
    public static StreamGeometry Settings { get; } = StreamGeometry.Parse(
        "M10.05,4.45 L10.61,2.1 L13.39,2.1 L13.95,4.45 L15.96,5.28 L18.02,4.01 L19.99,5.98 L18.72,8.04 " +
        "L19.55,10.05 L21.9,10.61 L21.9,13.39 L19.55,13.95 L18.72,15.96 L19.99,18.02 L18.02,19.99 L15.96,18.72 " +
        "L13.95,19.55 L13.39,21.9 L10.61,21.9 L10.05,19.55 L8.04,18.72 L5.98,19.99 L4.01,18.02 L5.28,15.96 " +
        "L4.45,13.95 L2.1,13.39 L2.1,10.61 L4.45,10.05 L5.28,8.04 L4.01,5.98 L5.98,4.01 L8.04,5.28 Z " +
        "M16.2,12 A4.2,4.2 0 1 1 7.8,12 A4.2,4.2 0 1 1 16.2,12 Z");

    /// <summary>
    /// Gets the about icon: a solid radius-10 disc with a lowercase "i" cut out, built from a radius-1.4 dot and a
    /// 2.2px-wide rounded-cap stem.
    /// </summary>
    public static StreamGeometry Info { get; } = StreamGeometry.Parse(
        "M22,12 A10,10 0 1 1 2,12 A10,10 0 1 1 22,12 Z " +
        "M13.4,7.7 A1.4,1.4 0 1 1 10.6,7.7 A1.4,1.4 0 1 1 13.4,7.7 Z " +
        "M10.9,11.7 A1.1,1.1 0 0 1 13.1,11.7 V15.9 A1.1,1.1 0 0 1 10.9,15.9 Z");

    /// <summary>
    /// Gets the browse-folder icon: an open folder made of a tabbed back panel and a slanted front flap
    /// (a parallelogram leaning 16 degrees), drawn as two non-overlapping sub-paths separated by a visible gap.
    /// </summary>
    public static StreamGeometry FolderOpen { get; } = StreamGeometry.Parse(
        "M2.5,18.5 V6.5 C2.5,5.4 3.4,4.5 4.5,4.5 H8.4 C8.9,4.5 9.4,4.7 9.8,5.1 L11.3,6.6 " +
        "C11.7,7 12.2,7.2 12.7,7.2 H17 C18.1,7.2 19,8.1 19,9.2 V9.9 H8.6 C7.1,9.9 5.7,10.7 5.1,12.1 Z " +
        "M8.8,11.2 H21 C21.7,11.2 22.2,11.9 22,12.6 L19.9,19.9 C19.7,20.4 19.2,20.8 18.6,20.8 H5.2 " +
        "C4.5,20.8 4,20.1 4.2,19.4 L6.3,12.1 C6.5,11.6 7,11.2 7.6,11.2 Z");

    /// <summary>
    /// Gets the show-password icon: an almond formed by two 115-degree circular arcs (radius 11.84 through
    /// (12,6.5) and (12,17.5)) holding an even-odd iris: a radius-3.5 hole around a filled radius-1.5 pupil.
    /// </summary>
    public static StreamGeometry Eye { get; } = StreamGeometry.Parse(
        "M2,12 A11.84,11.84 0 0 1 22,12 A11.84,11.84 0 0 1 2,12 Z " +
        "M15.5,12 A3.5,3.5 0 1 1 8.5,12 A3.5,3.5 0 1 1 15.5,12 Z " +
        "M13.5,12 A1.5,1.5 0 1 1 10.5,12 A1.5,1.5 0 1 1 13.5,12 Z");

    /// <summary>
    /// Gets the generate-password icon: a four-point star drawn with four quadratic curves pulled toward the
    /// center (arm length 7), plus a smaller copy (arm length 3.7) at the upper right.
    /// </summary>
    public static StreamGeometry Sparkle { get; } = StreamGeometry.Parse(
        "M10,6.5 Q11.6,11.9 17,13.5 Q11.6,15.1 10,20.5 Q8.4,15.1 3,13.5 Q8.4,11.9 10,6.5 Z " +
        "M17.8,2.5 Q18.7,5.3 21.5,6.2 Q18.7,7.1 17.8,9.9 Q16.9,7.1 14.1,6.2 Q16.9,5.3 17.8,2.5 Z");

    /// <summary>
    /// Gets the copy-to-clipboard icon: a board outline with an integral raised clip bump (a single sub-path with
    /// rounded corners) and two rounded-capsule text lines cut out of the body.
    /// </summary>
    public static StreamGeometry Clipboard { get; } = StreamGeometry.Parse(
        "M7,4 H8.6 V3.1 C8.6,2.5 9,2.1 9.6,2.1 H14.4 C15,2.1 15.4,2.5 15.4,3.1 V4 H17 C18.1,4 19,4.9 19,6 " +
        "V19.9 C19,21 18.1,21.9 17,21.9 H7 C5.9,21.9 5,21 5,19.9 V6 C5,4.9 5.9,4 7,4 Z " +
        "M9.4,9 H14.6 A0.9,0.9 0 0 1 14.6,10.8 H9.4 A0.9,0.9 0 0 1 9.4,9 Z " +
        "M9.4,13.2 H12.6 A0.9,0.9 0 0 1 12.6,15 H9.4 A0.9,0.9 0 0 1 9.4,13.2 Z");

    /// <summary>
    /// Gets the warning icon: a rounded-corner isosceles triangle (Bezier apex at y=3.2, base at y=20.7) with an
    /// exclamation mark cut out, built from a capsule stem and a radius-1.2 dot.
    /// </summary>
    public static StreamGeometry Warning { get; } = StreamGeometry.Parse(
        "M10.5,4.1 C11.2,2.9 12.8,2.9 13.5,4.1 L20.9,17.9 C21.6,19.2 20.7,20.7 19.2,20.7 H4.8 " +
        "C3.3,20.7 2.4,19.2 3.1,17.9 Z " +
        "M11,9.2 A1,1 0 0 1 13,9.2 V13.6 A1,1 0 0 1 11,13.6 Z " +
        "M13.2,17.4 A1.2,1.2 0 1 1 10.8,17.4 A1.2,1.2 0 1 1 13.2,17.4 Z");

    /// <summary>
    /// Gets the error icon: a solid radius-10 disc with a twelve-vertex "X" polygon cut out (2.2px stroke width,
    /// tips 4.6 from the center along the diagonals).
    /// </summary>
    public static StreamGeometry ErrorCircle { get; } = StreamGeometry.Parse(
        "M22,12 A10,10 0 1 1 2,12 A10,10 0 1 1 22,12 Z " +
        "M9.53,7.97 L12,10.44 L14.47,7.97 L16.03,9.53 L13.56,12 L16.03,14.47 L14.47,16.03 L12,13.56 " +
        "L9.53,16.03 L7.97,14.47 L10.44,12 L7.97,9.53 Z");

    /// <summary>
    /// Gets the success icon: a solid radius-10 disc with the same mitred 2px checkmark polygon as
    /// <see cref="ShieldCheck"/> cut out, optically centered in the disc.
    /// </summary>
    public static StreamGeometry CheckCircle { get; } = StreamGeometry.Parse(
        "M22,12 A10,10 0 1 1 2,12 A10,10 0 1 1 22,12 Z " +
        "M7.4,12.2 L8.8,10.8 L10.8,12.8 L15.2,8.4 L16.6,9.8 L10.8,15.6 Z");

    /// <summary>
    /// Gets the encryption icon: a padlock built from a 2px half-ring shackle (outer radius 5) that stops 0.8px
    /// above a rounded-rectangle body, with a capsule keyhole cut out of the body's center.
    /// </summary>
    public static StreamGeometry Lock { get; } = StreamGeometry.Parse(
        "M7.5,10 H16.5 A2.5,2.5 0 0 1 19,12.5 V18.5 A2.5,2.5 0 0 1 16.5,21 H7.5 A2.5,2.5 0 0 1 5,18.5 V12.5 " +
        "A2.5,2.5 0 0 1 7.5,10 Z " +
        "M7,9.2 V7.5 A5,5 0 0 1 17,7.5 V9.2 H15 V7.5 A3,3 0 0 0 9,7.5 V9.2 Z " +
        "M11,13.6 A1,1 0 0 1 13,13.6 V17 A1,1 0 0 1 11,17 Z");

    /// <summary>
    /// Gets the key-derivation icon: a single contour tracing a radius-4.5 bow and a 2.2px shaft that ends in two
    /// rounded teeth, with a radius-1.8 hole cut out of the bow.
    /// </summary>
    public static StreamGeometry Key { get; } = StreamGeometry.Parse(
        "M11.9,10.9 H20 A1,1 0 0 1 21,11.9 V15.2 A0.8,0.8 0 0 1 20.2,16 H19.6 A0.8,0.8 0 0 1 18.8,15.2 V13.1 " +
        "H17.6 V14.8 A0.8,0.8 0 0 1 16.8,15.6 H16.2 A0.8,0.8 0 0 1 15.4,14.8 V13.1 H11.9 " +
        "A4.5,4.5 0 1 1 11.9,10.9 Z " +
        "M9.3,12 A1.8,1.8 0 1 1 5.7,12 A1.8,1.8 0 1 1 9.3,12 Z");

    /// <summary>
    /// Gets the compression icon: an archive box made of a rounded lid bar floating 1.5px above a body with
    /// rounded bottom corners, and a capsule handle slot cut out of the body.
    /// </summary>
    public static StreamGeometry Archive { get; } = StreamGeometry.Parse(
        "M4.5,4 H19.5 A1.5,1.5 0 0 1 21,5.5 V7.5 A1.5,1.5 0 0 1 19.5,9 H4.5 A1.5,1.5 0 0 1 3,7.5 V5.5 " +
        "A1.5,1.5 0 0 1 4.5,4 Z " +
        "M4.5,10.5 H19.5 V18 A2,2 0 0 1 17.5,20 H6.5 A2,2 0 0 1 4.5,18 Z " +
        "M9.9,13 H14.1 A0.9,0.9 0 0 1 14.1,14.8 H9.9 A0.9,0.9 0 0 1 9.9,13 Z");

    /// <summary>
    /// Gets the files icon: a page with rounded corners and a 45-degree cut top-right corner, with two
    /// rounded-capsule text lines of unequal length cut out of it.
    /// </summary>
    public static StreamGeometry Document { get; } = StreamGeometry.Parse(
        "M6.5,2.5 H13.5 L19.5,8.5 V19.5 A2,2 0 0 1 17.5,21.5 H6.5 A2,2 0 0 1 4.5,19.5 V4.5 A2,2 0 0 1 6.5,2.5 Z " +
        "M8.5,12 H15.5 A0.9,0.9 0 0 1 15.5,13.8 H8.5 A0.9,0.9 0 0 1 8.5,12 Z " +
        "M8.5,15.8 H13 A0.9,0.9 0 0 1 13,17.6 H8.5 A0.9,0.9 0 0 1 8.5,15.8 Z");

    /// <summary>
    /// Gets the duration icon: a solid radius-10 disc with a single L-shaped cut-out drawing the hour and minute
    /// hands at twelve and three o'clock, 1.8px wide with rounded ends.
    /// </summary>
    public static StreamGeometry Clock { get; } = StreamGeometry.Parse(
        "M22,12 A10,10 0 1 1 2,12 A10,10 0 1 1 22,12 Z " +
        "M11.1,6.6 A0.9,0.9 0 0 1 12.9,6.6 V11.1 H15.9 A0.9,0.9 0 0 1 15.9,12.9 H12 A0.9,0.9 0 0 1 11.1,12 Z");

    /// <summary>
    /// Gets the size icon: a database cylinder drawn as an elliptical cap (radii 7.5 by 3) over two slices whose
    /// top and bottom edges follow the same ellipse, separated by 1.4px gaps.
    /// </summary>
    public static StreamGeometry Database { get; } = StreamGeometry.Parse(
        "M19.5,6 A7.5,3 0 1 1 4.5,6 A7.5,3 0 1 1 19.5,6 Z " +
        "M4.5,7.4 A7.5,3 0 0 0 19.5,7.4 V11.6 A7.5,3 0 0 1 4.5,11.6 Z " +
        "M4.5,13 A7.5,3 0 0 0 19.5,13 V17.2 A7.5,3 0 0 1 4.5,17.2 Z");

    /// <summary>
    /// Gets the benchmark icon: a speedometer made of a half-ring dial (outer radius 9, inner radius 6.4) and a
    /// 2px capsule needle pointing up and to the right from the dial's center.
    /// </summary>
    public static StreamGeometry Gauge { get; } = StreamGeometry.Parse(
        "M3,17 A9,9 0 0 1 21,17 H18.4 A6.4,6.4 0 0 0 5.6,17 Z " +
        "M11.29,16.29 L14.79,12.79 A1,1 0 0 1 16.21,14.21 L12.71,17.71 A1,1 0 0 1 11.29,16.29 Z");
}
