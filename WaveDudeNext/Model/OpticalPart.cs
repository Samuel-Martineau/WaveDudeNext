// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Alexander Graef <alex@graef.cc>
//
// This file is part of WaveDude Next, a 2D wave simulator.
//
// Based on the work of
//   Jeroen Vleggaar (Huygens Optics), author of Wave Dude 2D, Copyright (C) 2026
//     <https://www.youtube.com/@HuygensOptics>, <https://www.huygensoptics.com>
//   0x23 (Diffraction Limited), author of WaveSimulator2D
//     <https://www.youtube.com/@DiffractionLimited>, <https://github.com/0x23/WaveSimulator2D>
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using WaveDudeNext.Parts;

namespace WaveDudeNext.Model;

/// <summary>
/// An optical part placed on the canvas: a lens or mirror, a shape (polygon, rectangle, ellipse, triangle, outline), a
/// diffusor, an interference filter, a simple grating, a facetted grating, a meta lens, a Fresnel lens, a fibre, a Bragg reflector, a GRIN lens, a logic gate or a zone plate (Jeroen Vleggaar's proposal, decided 2026-10-18;
/// OpticalParts.md is the reference). Parts are painted into the scene pixels before a run
/// (<see cref="Parts.PartPainter"/>), so the engines, the outline and the export see them as part of the scene: the
/// index into red, an absorber's damping into blue; green is never touched. Positions and lengths are canvas pixels; the
/// angle is in degrees, counter-clockwise on the screen, 0 pointing the part's axis along +x (the composite source's
/// convention). Refractive indices run from 1.00 to 2.55 in steps of 0.01 (red = index × 100). The class is the kind,
/// chosen when the part is added (<see cref="Project.AddPart(Type)"/>) and never changed.
/// </summary>
public abstract class OpticalPart : ObservableObject, IMovable, INamed
{
	/// <summary>Smallest refractive index a part can have.</summary>
	public const double MinIndex = 1.0;
	/// <summary>Largest refractive index a part can have (red 255).</summary>
	public const double MaxIndex = 2.55;
	/// <summary>Largest coating thickness in pixels.</summary>
	public const int MaxCoating = 50;
	/// <summary>Largest absorption (blue 255, the velocity damped away in one step).</summary>
	public const int MaxAbsorption = 255;
	/// <summary>Default absorption ramp in pixels.</summary>
	public const int DefaultAbsorptionRamp = 40;
	/// <summary>Largest absorption ramp in pixels.</summary>
	public const int MaxAbsorptionRamp = 400;

	private string _name = "";
	private bool _enabled = true;
	private int _x;
	private int _y;
	private double _angle;

	/// <summary>Name shown in the project tree and on the canvas; must be non-blank.</summary>
	[XmlAttribute]
	[Category("General")]
	[Description("Name shown in the project tree and on the canvas.")]
	public string Name
	{
		get => _name;
		set => SetProperty(ref _name, value);
	}

	/// <summary>Whether the part is painted into the scene; a disabled part has no effect on a run.</summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("General")]
	[Description("Paint the part into the scene; a disabled part has no effect on a run.")]
	public bool Enabled
	{
		get => _enabled;
		set => SetProperty(ref _enabled, value);
	}

	/// <summary>X of the part's centre in canvas pixels.</summary>
	[XmlAttribute]
	[Category("Position")]
	[Description("X of the part's centre in canvas pixels.")]
	[Range(0, int.MaxValue)]
	public int X
	{
		get => _x;
		set => SetProperty(ref _x, value);
	}

	/// <summary>Y of the part's centre in canvas pixels.</summary>
	[XmlAttribute]
	[Category("Position")]
	[Description("Y of the part's centre in canvas pixels.")]
	[Range(0, int.MaxValue)]
	public int Y
	{
		get => _y;
		set => SetProperty(ref _y, value);
	}

	/// <summary>Rotation about the centre in degrees, counter-clockwise on the screen; 0 points the part's axis along +x.</summary>
	[XmlAttribute]
	[DefaultValue(0.0)]
	[Range(-180.0, 180.0, MinimumIsExclusive = true)]
	[Category("Position")]
	[DisplayName("Angle")]
	[Description("Rotation about the centre in degrees, counter-clockwise on the screen. 0 points the part's axis to the right.")]
	public double Angle
	{
		get => _angle;
		set => SetProperty(ref _angle, value);
	}

	/// <summary>The English stem of a new part's name ("Lens", "Bragg reflector", …), numbered by <see cref="Project.AddPart(Type)"/>; fixed per subclass.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public abstract string NameStem { get; }

	/// <summary>
	/// Gives a part made by <see cref="Project.AddPart(Type)"/> what a new part starts with and a loaded one must not get
	/// (a fibre's default path: <see cref="XmlSerializer"/> appends a file's items to the list it finds). Does nothing by default.
	/// </summary>
	public virtual void PrepareNew()
	{
	}

	/// <summary>Half-extent of the part along its axis in canvas pixels, so the rotation handle can sit beyond it. UI thread.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public abstract double AxisReach { get; }

	/// <summary>
	/// The part's outline for drawing, as points in the part frame (relative to the centre, before rotation, canvas
	/// pixels): a closed polygon unless <see cref="OutlineClosed"/> says otherwise. Out-of-range values are guarded as
	/// the painter guards them, so the drawn outline is the painted one. UI thread.
	/// </summary>
	public abstract (double X, double Y)[] Outline();

	/// <summary>Whether <see cref="Outline"/> closes back to its first point (a fibre's path does not).</summary>
	[XmlIgnore]
	[Browsable(false)]
	public virtual bool OutlineClosed => true;

	/// <summary>Maps a point of the part frame to canvas coordinates: the centre at (X, Y), the frame turned by the angle (counter-clockwise on the screen).</summary>
	public (double X, double Y) ToCanvas(double x, double y)
	{
		double radians = Angle * Math.PI / 180.0;
		double cos = Math.Cos(radians);
		double sin = Math.Sin(radians);
		return (X + x * cos + y * sin, Y + y * cos - x * sin);
	}

	/// <summary>Maps canvas coordinates into the part frame (the inverse of <see cref="ToCanvas"/>).</summary>
	public (double X, double Y) ToFrame(double canvasX, double canvasY)
	{
		double radians = Angle * Math.PI / 180.0;
		double cos = Math.Cos(radians);
		double sin = Math.Sin(radians);
		double ox = canvasX - X;
		double oy = canvasY - Y;
		return (ox * cos - oy * sin, ox * sin + oy * cos);
	}

	/// <inheritdoc/>
	[XmlIgnore]
	[Browsable(false)]
	public IEnumerable<(int X, int Y)> Anchors => [(X, Y)];

	/// <inheritdoc/>
	public void MoveBy(int dx, int dy)
	{
		X += dx;
		Y += dy;
	}

	/// <summary>Clamps every value into its valid range; the angle is brought into (-180, 180].</summary>
	public virtual void Normalize()
	{
		Name ??= "";
		X = Math.Max(0, X);
		Y = Math.Max(0, Y);
		Angle = NormalizeAngle(Angle);
	}

	/// <summary>A deep copy with the same settings and no change subscribers.</summary>
	public abstract OpticalPart Clone();

	/// <summary>The name, for display in the project tree and property grids.</summary>
	public override string ToString() => Name;

	/// <summary>Brings an angle in degrees into (-180, 180]; a non-finite value becomes 0.</summary>
	protected static double NormalizeAngle(double degrees)
	{
		if (!double.IsFinite(degrees))
		{
			return 0.0;
		}
		if (degrees <= -180.0 || degrees > 180.0)
		{
			degrees -= 360.0 * Math.Ceiling((degrees - 180.0) / 360.0);
		}
		return degrees;
	}

	/// <summary>A refractive index clamped to <see cref="MinIndex"/>..<see cref="MaxIndex"/> and rounded to 0.01 (red = index × 100); a non-finite value becomes 1.</summary>
	protected static double NormalizeIndex(double index) =>
		double.IsFinite(index) ? Math.Round(Math.Clamp(index, MinIndex, MaxIndex), 2) : MinIndex;

	/// <summary>Deep-copies a point list.</summary>
	protected static ObservableCollection<PartPoint> ClonePoints(ObservableCollection<PartPoint> points) =>
		new(points.Select(p => new PartPoint { X = p.X, Y = p.Y }));
}

/// <summary>One vertex of an <see cref="OutlinePart"/>, in canvas pixels relative to the part's centre, before rotation.</summary>
public sealed class PartPoint : ObservableObject
{
	private int _x;
	private int _y;

	/// <summary>X relative to the part's centre in canvas pixels, before rotation.</summary>
	[XmlAttribute]
	[Category("Point")]
	[Description("X relative to the part's centre in canvas pixels, before rotation.")]
	public int X
	{
		get => _x;
		set => SetProperty(ref _x, value);
	}

	/// <summary>Y relative to the part's centre in canvas pixels, before rotation.</summary>
	[XmlAttribute]
	[Category("Point")]
	[Description("Y relative to the part's centre in canvas pixels, before rotation.")]
	public int Y
	{
		get => _y;
		set => SetProperty(ref _y, value);
	}

	/// <summary>The coordinates, for display.</summary>
	public override string ToString() => $"({X}, {Y})";
}

/// <summary>
/// How the index runs through an anti-reflection band (2026-10-29, the user's request: the author's drawn scenes give
/// a glass face a graded ramp, and the parts had only the layer).
/// </summary>
public enum CoatingProfile
{
	/// <summary>A band at the index √n throughout: a quarter-wave layer when it is λ / (4 √n) thick, the least reflecting then.</summary>
	Layer,
	/// <summary>
	/// The index rises over the band from the medium outside to the glass as a raised cosine, (1 − cos πt) / 2 of the
	/// depth over the thickness (<see cref="Parts.PartShape.GradedProfile"/>): broadband, and the thicker the better; the
	/// figures are under "Coatings" in OpticalParts.md.
	/// </summary>
	Graded,
}

/// <summary>
/// A refractive part with an anti-reflection band along its outline (a lens, a shape, a GRIN lens; 2026-10-29, the
/// user's request): the band's <see cref="Coating"/> thickness, its <see cref="CoatingProfile"/>, and which faces carry
/// it. A face is classed by its outward normal in the part frame: within 45° of −x', the side a source on the left
/// meets, it is the front, within 45° of +x' the back, otherwise a side; so a lens's first surface, second surface and
/// rim, and a rectangle's four faces when its length runs along the beam. Every face is coated by default.
/// </summary>
public abstract class CoatedPart : OpticalPart
{
	/// <summary>Default <see cref="CoatingProfile"/>.</summary>
	public const CoatingProfile DefaultCoatingProfile = CoatingProfile.Layer;

	private int _coating;
	private CoatingProfile _coatingProfile = DefaultCoatingProfile;
	private bool _frontCoated = true;
	private bool _backCoated = true;
	private bool _sidesCoated = true;

	/// <summary>Thickness in pixels of the anti-reflection band just inside the outline; 0 for none. A quarter-wave layer is λ / (4 √n) thick.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Range(0, MaxCoating)]
	[Category("Material")]
	[DisplayName("AR coating")]
	[Description("Thickness in pixels of the anti-reflection band just inside the outline; 0 for none. A quarter-wave layer is λ / (4 √n) pixels thick, λ being the wavelength the source form shows; a graded band is the better the thicker, from a wavelength on.")]
	public int Coating
	{
		get => _coating;
		set => SetProperty(ref _coating, value);
	}

	/// <summary>How the index runs through the band: a layer at √n, or a graded rise from the medium outside to the glass.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultCoatingProfile)]
	[Category("Material")]
	[DisplayName("Coating profile")]
	[Description("How the index runs through the band: a layer at √n, the quarter-wave idea, or a graded rise from the medium outside to the glass. A graded band reflects less than a layer once it is a wavelength thick or more, at any wavelength; a layer only at the wavelength it is cut for.")]
	public CoatingProfile CoatingProfile
	{
		get => _coatingProfile;
		set => SetProperty(ref _coatingProfile, value);
	}

	/// <summary>Whether the front faces carry the band: those whose outward normal lies within 45° of −x', the side a source on the left meets.</summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Material")]
	[DisplayName("Coat the front")]
	[Description("Put the band on the front: the faces turned toward a source on the left, a lens's first surface. Clear it to leave them bare, as the inner faces of a cavity are.")]
	public bool FrontCoated
	{
		get => _frontCoated;
		set => SetProperty(ref _frontCoated, value);
	}

	/// <summary>Whether the back faces carry the band: those whose outward normal lies within 45° of +x'.</summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Material")]
	[DisplayName("Coat the back")]
	[Description("Put the band on the back: the faces turned away from a source on the left, a lens's second surface. Clear it to leave them bare.")]
	public bool BackCoated
	{
		get => _backCoated;
		set => SetProperty(ref _backCoated, value);
	}

	/// <summary>Whether the other faces carry the band: a lens's rim, a slab's narrow ends.</summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Material")]
	[DisplayName("Coat the sides")]
	[Description("Put the band on the sides: the faces that are neither front nor back, a lens's rim, a slab's narrow ends. Clear it to leave them bare.")]
	public bool SidesCoated
	{
		get => _sidesCoated;
		set => SetProperty(ref _sidesCoated, value);
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Coating = Math.Clamp(Coating, 0, MaxCoating);
		if (!Enum.IsDefined(CoatingProfile))
		{
			CoatingProfile = DefaultCoatingProfile;
		}
	}
}

/// <summary>The shape of one surface of a <see cref="LensPart"/> as it reads: bowing outward, flat, or bowing inward (<see cref="LensPart.ShapeOf"/>).</summary>
public enum SurfaceShape
{
	/// <summary>No bow worth a pixel: a radius of 0, or a sag at the rim under half a pixel.</summary>
	Flat,
	/// <summary>Bowing outward, away from the lens.</summary>
	Convex,
	/// <summary>Bowing inward, into the lens.</summary>
	Concave,
}

/// <summary>
/// A lens, or a curved or flat mirror when <see cref="Mirror"/> is set: the strip of <see cref="Diameter"/> across
/// the axis between two spherical surfaces of radii <see cref="R1"/> (left, facing -x') and <see cref="R2"/>
/// (right), <see cref="Thickness"/> apart on the axis. A radius is signed: R1 &gt; 0 and R2 &lt; 0 bulge outward
/// (biconvex), 0 is flat. The diameter and the centre thickness are the lens's bulk; the radii fit them: a radius
/// too curved for them is raised to the smallest that fits (<see cref="FitRadii(int, int, double, double, int)"/>), so the lens always fills its
/// diameter and the edge thickness is never negative (the user's point, 2026-10-18: a diameter of 2000 with the
/// default radii gave a tiny lens, since the surfaces crossed long before the rim).
/// </summary>
public class LensPart : CoatedPart
{
	/// <summary>Default <see cref="RefractiveIndex"/>.</summary>
	public const double DefaultIndex = 1.5;
	/// <summary>Default <see cref="Diameter"/> in pixels.</summary>
	public const int DefaultDiameter = 200;
	/// <summary>Default <see cref="R1"/> in pixels.</summary>
	public const double DefaultR1 = 300.0;
	/// <summary>Default <see cref="R2"/> in pixels.</summary>
	public const double DefaultR2 = -300.0;
	/// <summary>Default <see cref="Thickness"/> in pixels.</summary>
	public const int DefaultThickness = 40;

	private bool _mirror;
	private double _refractiveIndex = DefaultIndex;
	private int _absorption;
	private int _absorptionRamp = DefaultAbsorptionRamp;
	private int _diameter = DefaultDiameter;
	private double _r1 = DefaultR1;
	private double _r2 = DefaultR2;
	private int _thickness = DefaultThickness;

	/// <inheritdoc/>
	public override string NameStem => "Lens";

	/// <summary>
	/// How many zones of equal width each curved surface is cut into from the axis to the rim, the arc restarted at the
	/// vertex plane at each zone's inner edge (a <see cref="FresnelLensPart"/>, 2026-11-07): 1 for the plain lens, whose
	/// surfaces are whole arcs. The sag that counts for the fit, the reach, the edge thickness and the vertex handles is
	/// the outer zone's (<see cref="ZoneSag(double, double, int, ZoneDivision)"/>), the deepest. Not stored here.
	/// </summary>
	[XmlIgnore]
	[Browsable(false)]
	public virtual int ZoneCount => 1;

	/// <summary>How the zones are divided over the radius (<see cref="Model.ZoneDivision"/>): equal in width unless a Fresnel lens says otherwise. Not stored here.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public virtual ZoneDivision ZoneDivision => ZoneDivision.EqualWidth;

	/// <summary>Whether the part is a mirror (red 0, a wall no wave gets through) instead of a lens; the index, the coating and the absorption are ignored then.</summary>
	[XmlAttribute]
	[DefaultValue(false)]
	[Category("Material")]
	[Description("Make the part a mirror instead of a lens. The refractive index, the coating and the absorption are ignored then.")]
	public bool Mirror
	{
		get => _mirror;
		set => SetProperty(ref _mirror, value);
	}

	/// <summary>
	/// <paramref name="name"/> with the word Lens replaced by Mirror when <paramref name="mirror"/> is set and Mirror by Lens
	/// when not, so "Lens 1" ticked as a mirror reads "Mirror 1" and unticked "Lens 1" again (2026-10-29, the user's design); a
	/// name with neither word is as given. The English stems, whatever the language, as <see cref="NameStem"/> is. The window
	/// applies it when the user ticks the box, not the setter, so a loaded, pasted or undone name stays what it was.
	/// </summary>
	public static string MirrorName(string name, bool mirror) =>
		mirror ? WordLens.Replace(name, "Mirror") : WordMirror.Replace(name, "Lens");

	private static readonly Regex WordLens = new(@"\bLens\b", RegexOptions.CultureInvariant);
	private static readonly Regex WordMirror = new(@"\bMirror\b", RegexOptions.CultureInvariant);

	/// <summary>Refractive index of the glass, 1.00 to 2.55 in steps of 0.01; not used by a mirror.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultIndex)]
	[Range(MinIndex, MaxIndex)]
	[Category("Material")]
	[DisplayName("Refractive index")]
	[Description("Refractive index of the glass, 1.00 to 2.55 in steps of 0.01.")]
	public double RefractiveIndex
	{
		get => _refractiveIndex;
		set => SetProperty(ref _refractiveIndex, value);
	}

	/// <summary>
	/// How strongly the part absorbs, 0 (not at all) to 255, painted into the scene's blue channel (the engines damp a
	/// cell's velocity by 1 − blue / 255 each step) where it rises over <see cref="AbsorptionRamp"/>; a mirror ignores it.
	/// </summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Range(0, MaxAbsorption)]
	[Category("Material")]
	[Description("How strongly the part absorbs waves, 0 (not at all) to 255, as the scene image's blue channel. It rises from nothing at the outline over the absorption ramp, so a wave enters the part instead of being reflected. A mirror ignores it.")]
	public int Absorption
	{
		get => _absorption;
		set => SetProperty(ref _absorption, value);
	}

	/// <summary>Depth in pixels over which the absorption rises from 0 at the outline to <see cref="Absorption"/>; at most half the part's thickness takes effect.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultAbsorptionRamp)]
	[Range(1, MaxAbsorptionRamp)]
	[Category("Material")]
	[DisplayName("Absorption ramp")]
	[Description("Depth in pixels over which the absorption rises from nothing at the outline to its full value; the longer, the less of a wave is reflected. A part thinner than twice the ramp reaches its full absorption in its middle.")]
	public int AbsorptionRamp
	{
		get => _absorptionRamp;
		set => SetProperty(ref _absorptionRamp, value);
	}

	/// <summary>Extent across the axis in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultDiameter)]
	[Range(2, int.MaxValue)]
	[Category("Shape")]
	[Description("Extent across the axis in pixels.")]
	public int Diameter
	{
		get => _diameter;
		set => SetProperty(ref _diameter, value);
	}

	/// <summary>Radius of the first surface (facing -x') in pixels: positive bulges outward, negative is concave, 0 is flat; raised when too curved for the diameter and thickness.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultR1)]
	[Category("Shape")]
	[DisplayName("R1")]
	[Description("Radius of the first surface in pixels: positive bulges outward, negative is concave, 0 is flat. A radius too curved for the diameter and the centre thickness is raised to the smallest that fits.")]
	public double R1
	{
		get => _r1;
		set
		{
			if (SetProperty(ref _r1, value))
			{
				OnPropertyChanged(nameof(IsConcave));
			}
		}
	}

	/// <summary>Radius of the second surface (facing +x') in pixels: negative bulges outward, positive is concave, 0 is flat; raised when too curved for the diameter and thickness.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultR2)]
	[Category("Shape")]
	[DisplayName("R2")]
	[Description("Radius of the second surface in pixels: negative bulges outward, positive is concave, 0 is flat. A radius too curved for the diameter and the centre thickness is raised to the smallest that fits.")]
	public double R2
	{
		get => _r2;
		set
		{
			if (SetProperty(ref _r2, value))
			{
				OnPropertyChanged(nameof(IsConcave));
			}
		}
	}

	/// <summary>
	/// Whether the lens diverges: the lensmaker's sum of curvatures, 1/R1 − 1/R2, is negative (a flat surface counts
	/// 0). Setting it to the other state turns the lens around by flipping the sign of both radii, so biconvex becomes
	/// biconcave and plano-convex plano-concave (the user's proposal, 2026-10-18). Not stored: the radii are.
	/// </summary>
	[XmlIgnore]
	[Category("Shape")]
	[DisplayName("Concave (diverging)")]
	[Description("Whether the lens diverges, its surfaces curving inward. Ticking it turns the lens around by flipping the sign of both radii; a flat surface stays flat.")]
	public bool IsConcave
	{
		get => (R1 == 0.0 ? 0.0 : 1.0 / R1) - (R2 == 0.0 ? 0.0 : 1.0 / R2) < 0.0;
		set
		{
			if (value != IsConcave)
			{
				R1 = -R1;
				R2 = -R2;
			}
		}
	}

	/// <summary>Distance between the two surfaces on the axis in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultThickness)]
	[Range(1, int.MaxValue)]
	[Category("Shape")]
	[DisplayName("Centre thickness")]
	[Description("Distance between the two surfaces on the axis in pixels.")]
	public int Thickness
	{
		get => _thickness;
		set => SetProperty(ref _thickness, value);
	}

	/// <inheritdoc/>
	public override double AxisReach
	{
		get
		{
			(double r1, double r2) = FittedRadii();
			double half = Math.Max(2, Diameter) / 2.0;
			return Math.Max(1, Thickness) / 2.0 + Math.Max(ZoneSag(r1, half, ZoneCount, ZoneDivision), ZoneSag(r2, half, ZoneCount, ZoneDivision));
		}
	}

	/// <summary>Points per surface of the drawn outline.</summary>
	private const int OutlineSteps = 32;

	/// <inheritdoc/>
	public override (double X, double Y)[] Outline()
	{
		(double r1, double r2) = FittedRadii();
		double half = Math.Max(2, Diameter) / 2.0;
		double vertex = Math.Max(1, Thickness) / 2.0;
		var points = new (double X, double Y)[2 * (OutlineSteps + 1)];
		for (int i = 0; i <= OutlineSteps; i++)
		{
			// down the first surface, then up the second
			double y = -half + 2.0 * half * i / OutlineSteps;
			points[i] = (-vertex + SurfaceSag(r1, y), y);
			points[2 * OutlineSteps + 1 - i] = (vertex + SurfaceSag(r2, y), y);
		}
		return points;
	}

	/// <summary>Where a surface of signed <paramref name="radius"/> lies along x' at <paramref name="y"/> from the axis, relative to its vertex: 0 for flat, positive toward +x'.</summary>
	public static double SurfaceSag(double radius, double y)
	{
		if (radius == 0.0)
		{
			return 0.0;
		}
		double inside = Math.Max(0.0, radius * radius - y * y);
		return radius - Math.CopySign(Math.Sqrt(inside), radius);
	}

	/// <summary>Thickness at the rim in pixels: the centre thickness less what the convex surfaces take, plus what the concave ones add; never negative after <see cref="FitRadii(int, int, double, double, int)"/>.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double EdgeThickness
	{
		get
		{
			(double r1, double r2) = FittedRadii();
			double half = Math.Max(2, Diameter) / 2.0;
			return Math.Max(1, Thickness) - Math.CopySign(ZoneSag(r1, half, ZoneCount, ZoneDivision), r1) + Math.CopySign(ZoneSag(r2, half, ZoneCount, ZoneDivision), r2);
		}
	}

	/// <summary><see cref="ZoneSag(double, double, int, ZoneDivision)"/> for zones of equal width.</summary>
	public static double ZoneSag(double radius, double half, int zones) => ZoneSag(radius, half, zones, ZoneDivision.EqualWidth);

	/// <summary>
	/// How far the deepest of <paramref name="zones"/> zones of a surface of radius <paramref name="radius"/> bows out
	/// beyond its own start at the vertex plane, in pixels: the whole sag for one zone (<see cref="Sag"/>); for zones of
	/// equal width the outer zone's, the sag at <paramref name="half"/> less the sag at its inner edge, since the sag grows
	/// ever faster outward; for zones of equal depth every zone's, the whole sag over the count. The fit and the edge
	/// thickness reckon with it.
	/// </summary>
	public static double ZoneSag(double radius, double half, int zones, ZoneDivision division) =>
		zones <= 1 ? Sag(radius, half)
		: division == ZoneDivision.EqualDepth ? Sag(radius, half) / zones
		: Sag(radius, half) - Sag(radius, half * (zones - 1) / zones);

	/// <summary>
	/// Where zone boundary <paramref name="k"/> (0 the axis, <paramref name="zones"/> the rim) of a surface of radius
	/// <paramref name="radius"/> lies from the axis, in pixels: k / zones of <paramref name="half"/> for zones of equal
	/// width; for zones of equal depth where the sag reaches k / zones of the rim's, √(R² − (R − k·S/N)²), the zones
	/// narrowing outward (a flat surface is divided by width, having no sag).
	/// </summary>
	public static double ZoneBound(double radius, double half, int zones, ZoneDivision division, int k)
	{
		zones = Math.Max(1, zones);
		k = Math.Clamp(k, 0, zones);
		if (division == ZoneDivision.EqualWidth || radius == 0.0 || k == zones)
		{
			return half * k / zones;
		}
		double a = Math.Abs(radius);
		double along = a - Sag(radius, half) * k / zones;
		return Math.Sqrt(Math.Max(0.0, a * a - along * along));
	}

	/// <summary>The zone, 0 to <paramref name="zones"/> − 1, that <paramref name="across"/> pixels from the axis lies in, by the division's boundaries (<see cref="ZoneBound"/>).</summary>
	public static int ZoneIndex(double radius, double half, int zones, ZoneDivision division, double across)
	{
		zones = Math.Max(1, zones);
		double share = division == ZoneDivision.EqualWidth || radius == 0.0 || Sag(radius, half) <= 0.0
			? across / half
			: Sag(radius, Math.Min(across, half)) / Sag(radius, half);
		return Math.Clamp((int)(share * zones), 0, zones - 1);
	}

	/// <summary>Where the profile of a surface of signed <paramref name="radius"/> meets the rim, relative to its vertex, with <paramref name="zones"/> zones: <see cref="SurfaceSag"/> at the rim less the sag at the outer zone's inner edge.</summary>
	private static double ProfileSag(double radius, double half, int zones, ZoneDivision division) =>
		zones <= 1 ? SurfaceSag(radius, half) : SurfaceSag(radius, half) - SurfaceSag(radius, ZoneBound(radius, half, zones, division, zones - 1));

	/// <summary>The radii as this lens uses them: <see cref="FitRadii(int, int, double, double, int, ZoneDivision)"/> of its values, <see cref="ZoneCount"/> and <see cref="ZoneDivision"/>.</summary>
	protected (double R1, double R2) FittedRadii() => FitRadii(Diameter, Thickness, R1, R2, ZoneCount, ZoneDivision);

	/// <summary>
	/// The shape of a surface whose radius, signed so that positive bows outward, is <paramref name="outward"/> on a lens of
	/// <paramref name="diameter"/>: flat for 0 or when its sag at the rim is under half a pixel (the rule of
	/// <see cref="SetVertex(int, double, double, double)"/>; 2026-10-29, the user's report: Example 1's mirror of radii 14401
	/// on 104 px, a sag of a tenth of a pixel, showed as biconvex), else convex or concave by the sign. R1 is signed so; R2
	/// bows outward when negative, so it is passed negated.
	/// </summary>
	public static SurfaceShape ShapeOf(double outward, int diameter) =>
		!double.IsFinite(outward) || Sag(outward, Math.Max(2, diameter) / 2.0) < 0.5 ? SurfaceShape.Flat : outward > 0.0 ? SurfaceShape.Convex : SurfaceShape.Concave;

	/// <summary>How far a surface of radius <paramref name="radius"/> bows out beyond its vertex at <paramref name="half"/> (half the diameter) from the axis, in pixels; 0 for a flat surface.</summary>
	public static double Sag(double radius, double half)
	{
		if (radius == 0.0)
		{
			return 0.0;
		}
		double a = Math.Abs(radius);
		double h = Math.Min(a, half);
		return a - Math.Sqrt(a * a - h * h);
	}

	/// <summary>
	/// The radii as the lens uses them: a non-finite or zero radius is flat; a magnitude below half the diameter
	/// becomes half the diameter (the arc must span the rim); and when the convex surfaces (R1 &gt; 0, R2 &lt; 0)
	/// would bow out by more than the centre thickness plus what the concave ones add, they are flattened in
	/// proportion to the radius at which their sags just fit, so the surfaces meet at the rim and not before it.
	/// Shared by <see cref="Normalize"/>, the painter and the readouts, so all see the same lens.
	/// </summary>
	public static (double R1, double R2) FitRadii(int diameter, int thickness, double r1, double r2) => FitRadii(diameter, thickness, r1, r2, 1);

	/// <summary>
	/// <see cref="FitRadii(int, int, double, double)"/> for a surface cut into <paramref name="zones"/> zones (a Fresnel lens):
	/// the sag that must fit is the outer zone's (<see cref="ZoneSag(double, double, int, ZoneDivision)"/>), the deepest, so a thin plate holds radii a whole
	/// lens could not; and the outer zone's outer edge is where two surfaces would meet first, since within every zone and
	/// from zone to zone the convex surfaces' loss outgrows the concave ones' gain.
	/// </summary>
	public static (double R1, double R2) FitRadii(int diameter, int thickness, double r1, double r2, int zones) =>
		FitRadii(diameter, thickness, r1, r2, zones, ZoneDivision.EqualWidth);

	/// <summary><see cref="FitRadii(int, int, double, double, int)"/> with the zones divided as <paramref name="division"/> says: the deepest zone's sag (<see cref="ZoneSag(double, double, int, ZoneDivision)"/>) must fit.</summary>
	public static (double R1, double R2) FitRadii(int diameter, int thickness, double r1, double r2, int zones, ZoneDivision division)
	{
		double half = Math.Max(2, diameter) / 2.0;
		zones = Math.Max(1, zones);
		r1 = GuardRadius(r1, half);
		r2 = GuardRadius(r2, half);
		double sag1 = ZoneSag(r1, half, zones, division);
		double sag2 = ZoneSag(r2, half, zones, division);
		double convex = (r1 > 0.0 ? sag1 : 0.0) + (r2 < 0.0 ? sag2 : 0.0);
		double budget = Math.Max(1, thickness) + (r1 < 0.0 ? sag1 : 0.0) + (r2 > 0.0 ? sag2 : 0.0);
		// a hair over the budget (the floating point of a stretched lens's sags) is no case for a refit
		if (convex > budget + 1e-9)
		{
			double scale = budget / convex;
			if (r1 > 0.0)
			{
				r1 = RadiusForSag(half, zones, division, sag1 * scale);
			}
			if (r2 < 0.0)
			{
				r2 = -RadiusForSag(half, zones, division, sag2 * scale);
			}
		}
		return (r1, r2);
	}

	/// <summary>The radius whose deepest zone's sag (<see cref="ZoneSag(double, double, int, ZoneDivision)"/>) at <paramref name="half"/> from the axis with <paramref name="zones"/> zones is <paramref name="sag"/>, rounded up to a whole pixel (a flatter surface still fits).</summary>
	private static double RadiusForSag(double half, int zones, ZoneDivision division, double sag) => Math.Ceiling(RadiusOfZoneSag(half, zones, division, sag));

	/// <summary>
	/// The radius, at least <paramref name="half"/>, whose deepest zone's sag (<see cref="ZoneSag(double, double, int, ZoneDivision)"/>)
	/// is <paramref name="sag"/>: the sagitta (h² + s²) / 2s of the whole sag for one zone and for zones of equal depth
	/// (whose whole sag is the count times the zone's); for zones of equal width found by bisection, the outer zone's sag
	/// falling steadily from the hemisphere's at a radius of half toward 0 as the radius grows (a sag the hemisphere
	/// cannot reach gives half).
	/// </summary>
	private static double RadiusOfZoneSag(double half, int zones, ZoneDivision division, double sag)
	{
		if (zones <= 1 || division == ZoneDivision.EqualDepth)
		{
			double whole = Math.Min(half, sag * Math.Max(1, zones));
			return (half * half + whole * whole) / (2.0 * whole);
		}
		if (sag >= ZoneSag(half, half, zones))
		{
			return half;
		}
		double low = half, high = half * 2.0;
		while (ZoneSag(high, half, zones) > sag)
		{
			high *= 2.0;
		}
		for (int i = 0; i < 100 && high - low > 1e-9 * high; i++)
		{
			double mid = (low + high) / 2.0;
			if (ZoneSag(mid, half, zones) > sag)
			{
				low = mid;
			}
			else
			{
				high = mid;
			}
		}
		return high;
	}

	/// <summary>
	/// Sets the diameter and the centre thickness the size grip stands for by stretching the lens as a rectangle's or an
	/// ellipse's grip stretches theirs (2026-10-26, Jeroen's report and the user's rule after it: a grip dragged a little inward
	/// had the radii fitted flat and the lens was a rectangle; held sags made a stretched lens a slab): the diameter stretches
	/// the lens across, the profile along the axis held; the thickness stretches the whole profile along the axis, the sags
	/// and the edge with it, so a thinner lens is the same lens squashed and never a slab; pulled out diagonally the lens
	/// scales as a whole, its shape kept. The surfaces are refitted as circles through the stretched rim and vertex (a sag
	/// beyond the half diameter is a hemisphere's), the radii to a hundredth of a pixel (<see cref="RadiusOfSag"/>). Nothing
	/// stops the grip but a thickness of 1. Typed values are fitted as before.
	/// </summary>
	/// <param name="diameter">The diameter asked for, in pixels.</param>
	/// <param name="thickness">The centre thickness asked for, in pixels.</param>
	public void SetExtents(int diameter, int thickness)
	{
		(double r1, double r2) = FittedRadii();
		double half = Math.Max(2, Diameter) / 2.0;
		double grown = Math.Max(2, diameter) / 2.0;
		double along = Math.Max(1, thickness) / (double)Math.Max(1, Thickness);
		if (grown == half && along == 1.0)
		{
			return;
		}
		// the sags signed as the radii are: surface 1 convex is positive, surface 2 convex is negative (the profile's at the rim: the outer zone's)
		double sag1 = Math.Clamp(ProfileSag(r1, half, ZoneCount, ZoneDivision) * along, -grown, grown);
		double sag2 = Math.Clamp(ProfileSag(r2, half, ZoneCount, ZoneDivision) * along, -grown, grown);
		// the thicker value first, so no state in between has the radii fitted
		if (thickness > Thickness)
		{
			Thickness = Math.Max(1, thickness);
		}
		Diameter = Math.Max(2, diameter);
		R1 = RadiusOfSag(1, grown, sag1);
		R2 = RadiusOfSag(2, grown, sag2);
		Thickness = Math.Max(1, thickness);
	}

	/// <summary>
	/// The radius, signed as <paramref name="sag"/> is, whose surface <paramref name="surface"/> (1 or 2) has that sag at
	/// <paramref name="half"/> from the axis: the sagitta (h² + s²) / 2s; 0 for no sag. Rounded to a hundredth of a pixel
	/// (2026-10-28, Jeroen's report: a dragged vertex or grip left sixteen decimals in the box and the file), surface 1's
	/// towards +∞ and surface 2's towards −∞, which makes a convex surface a hair flatter and a concave one a hair deeper, never
	/// the other way, so the rounding never brings on the refit of <see cref="FitRadii(int, int, double, double, int)"/> for a lens whose surfaces meet at the rim.
	/// </summary>
	private double RadiusOfSag(int surface, double half, double sag)
	{
		if (sag == 0.0)
		{
			return 0.0;
		}
		// with zones the sag is the outer zone's (the profile at the rim), the radius found by bisection
		double hundredths = Math.CopySign(RadiusOfZoneSag(half, ZoneCount, ZoneDivision, Math.Abs(sag)), sag) * 100.0;
		return (surface == 1 ? Math.Ceiling(hundredths) : Math.Floor(hundredths)) / 100.0;
	}

	/// <summary>Where the vertex of surface <paramref name="surface"/> (1 the left, 2 the right) sits on the axis in the part frame: ∓<see cref="Thickness"/> / 2.</summary>
	public double VertexX(int surface) => (surface == 1 ? -1 : 1) * Math.Max(1, Thickness) / 2.0;

	/// <summary>Where surface <paramref name="surface"/> meets the rim, on the axis in the part frame: its vertex plus its sag at the half diameter, the radii as fitted.</summary>
	public double RimX(int surface)
	{
		(double r1, double r2) = FittedRadii();
		return VertexX(surface) + ProfileSag(surface == 1 ? r1 : r2, Math.Max(2, Diameter) / 2.0, ZoneCount, ZoneDivision);
	}

	/// <summary>
	/// Moves the vertex of surface <paramref name="surface"/> to <paramref name="x"/> on the axis (part frame), the surface's
	/// rim and the other surface held (2026-10-26, Jeroen's proposal, a handle on each vertex): the sag at the rim changes and
	/// with it the radius; pulled outward the surface bulges, pushed through the rim plane it is flat (within half a pixel),
	/// then concave, a hemisphere at most either way. The centre thickness follows, a whole pixel of at least 1, and the
	/// part's position moves to the new centre, rounded to a pixel. <paramref name="rim"/> and <paramref name="other"/> are
	/// where the rim and the other vertex are (a drag captures them as it starts, so the position's rounding never creeps
	/// into them).
	/// </summary>
	public void SetVertex(int surface, double x, double rim, double other)
	{
		double half = Math.Max(2, Diameter) / 2.0;
		int side = surface == 1 ? -1 : 1;
		int thickness = Math.Max(1, (int)Math.Round(side * (x - other)));
		x = other + side * thickness;
		double sag = Math.Clamp(side * (x - rim), -half, half);
		if (Math.Abs(sag) < 0.5)
		{
			sag = 0.0;
		}
		// the sagitta: surface 1 bulging left is R1 > 0, surface 2 bulging right is R2 < 0
		double radius = RadiusOfSag(surface, half, -side * sag);
		bool thicker = thickness > Thickness;
		if (thicker)
		{
			Thickness = thickness;
		}
		if (surface == 1)
		{
			R1 = radius;
		}
		else
		{
			R2 = radius;
		}
		if (!thicker)
		{
			Thickness = thickness;
		}
		(double cx, double cy) = ToCanvas((x + other) / 2.0, 0.0);
		X = (int)Math.Round(cx);
		Y = (int)Math.Round(cy);
	}

	/// <summary>Moves a vertex as <see cref="SetVertex(int, double, double, double)"/> does, the rim and the other vertex where they are now (a keyboard step).</summary>
	public void SetVertex(int surface, double x) => SetVertex(surface, x, RimX(surface), VertexX(surface == 1 ? 2 : 1));

	/// <summary>A radius of at least <paramref name="half"/> in magnitude, its sign kept; a non-finite or zero one is flat.</summary>
	private static double GuardRadius(double radius, double half) =>
		!double.IsFinite(radius) || radius == 0.0 ? 0.0 : Math.Abs(radius) < half ? Math.CopySign(half, radius) : radius;

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		RefractiveIndex = NormalizeIndex(RefractiveIndex);
		Absorption = Math.Clamp(Absorption, 0, MaxAbsorption);
		AbsorptionRamp = Math.Clamp(AbsorptionRamp, 1, MaxAbsorptionRamp);
		Diameter = Math.Max(2, Diameter);
		Thickness = Math.Max(1, Thickness);
		(double r1, double r2) = FittedRadii();
		R1 = r1;
		R2 = r2;
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<LensPart>();
}

/// <summary>
/// A Fresnel lens (the user's request, 2026-11-07): the lens of <see cref="LensPart"/> with each curved surface cut into
/// <see cref="Zones"/> zones from the axis to the rim, the arc restarted at the vertex plane at each zone's inner edge,
/// so the glass between the arcs is cut away and a thin plate bends light as the thick lens would (the user's choices:
/// both curved surfaces, a flat surface staying flat). The zones are divided as <see cref="Division"/> says: equal in
/// width, or equal in depth as a lighthouse lens's elements are (the user's request the same day), each zone then taking
/// the same share of the rim's sag, so they narrow outward. Within zone k, |y'| from its boundary y'ₖ to y'ₖ₊₁
/// (<see cref="LensPart.ZoneBound"/>), a surface lies at s(y') − s(y'ₖ) from its vertex; at a zone boundary a step across
/// the axis leads back to the vertex plane. The sag that counts for the fit
/// (<see cref="LensPart.FitRadii(int, int, double, double, int, ZoneDivision)"/>), the edge thickness and the vertex handles
/// is the deepest zone's. A new one is a plate of <see cref="DefaultPlateThickness"/>, <see cref="DefaultDiameter"/>
/// across with radii of <see cref="DefaultRadius"/> (<see cref="PrepareNew"/>; a loaded one has the file's values). The
/// lens's Concave box is not shown for it: a surface is hollowed by a negative radius (the user's choice).
/// </summary>
public sealed class FresnelLensPart : LensPart
{
	/// <summary>Default <see cref="Zones"/>.</summary>
	public const int DefaultZones = 4;
	/// <summary>Largest <see cref="Zones"/>.</summary>
	public const int MaxZones = 100;
	/// <summary>A new Fresnel lens's centre thickness in pixels (the user's choice, 2026-11-07): a thin plate.</summary>
	public const int DefaultPlateThickness = 20;
	/// <summary>A new Fresnel lens's diameter in pixels (the user's choice, 2026-11-07: about 500).</summary>
	public new const int DefaultDiameter = 500;
	/// <summary>A new Fresnel lens's radii in pixels, R1 and −R2: with four zones of equal width on a diameter of 500 the outer zone's sag is 9.2 px a surface, so the two fit in the plate of 20.</summary>
	public const double DefaultRadius = 1500.0;

	private int _zones = DefaultZones;
	private ZoneDivision _division = ZoneDivision.EqualWidth;

	/// <inheritdoc/>
	public override string NameStem => "Fresnel lens";

	/// <summary>How the zones are divided over the radius: equal in width, or equal in depth (each zone the same sag, the zones narrowing outward, as in a lighthouse lens).</summary>
	[XmlAttribute]
	[DefaultValue(ZoneDivision.EqualWidth)]
	[Category("Shape")]
	[DisplayName("Zone division")]
	[Description("How the zones are divided from the axis to the rim: equal in width, or equal in depth, each zone cut to the same depth and the zones narrowing outward, as the elements of a lighthouse lens are.")]
	public ZoneDivision Division
	{
		get => _division;
		set
		{
			if (SetProperty(ref _division, value))
			{
				OnPropertyChanged(nameof(ZoneDivision));
			}
		}
	}

	/// <inheritdoc/>
	public override ZoneDivision ZoneDivision => Enum.IsDefined(Division) ? Division : ZoneDivision.EqualWidth;

	/// <summary>Number of zones each curved surface is cut into from the axis to the rim, 1 to <see cref="MaxZones"/>, divided by <see cref="Division"/>; 1 is the plain lens.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultZones)]
	[Range(1, MaxZones)]
	[Category("Shape")]
	[Description("Number of zones each curved surface is cut into, from the axis to the rim, equal in width or in depth as the zone division says. At each zone's inner edge the arc starts again at the vertex plane, so the plate stays thin; 1 is a plain lens.")]
	public int Zones
	{
		get => _zones;
		set
		{
			if (SetProperty(ref _zones, value))
			{
				OnPropertyChanged(nameof(ZoneCount));
			}
		}
	}

	/// <inheritdoc/>
	public override int ZoneCount => Math.Clamp(Zones, 1, MaxZones);

	/// <summary>A new Fresnel lens is a thin plate of <see cref="DefaultPlateThickness"/>, <see cref="DefaultDiameter"/> across, its radii <see cref="DefaultRadius"/>.</summary>
	public override void PrepareNew()
	{
		Diameter = DefaultDiameter;
		R1 = DefaultRadius;
		R2 = -DefaultRadius;
		Thickness = DefaultPlateThickness;
	}

	/// <summary>The profile with its zones: each zone's arc, and at each zone boundary the step back to the vertex plane, down the first surface and up the second.</summary>
	public override (double X, double Y)[] Outline()
	{
		(double r1, double r2) = FittedRadii();
		double half = Math.Max(2, Diameter) / 2.0;
		double vertex = Math.Max(1, Thickness) / 2.0;
		List<(double X, double Y)> first = Profile(-vertex, r1, half, ZoneCount, ZoneDivision);
		List<(double X, double Y)> second = Profile(vertex, r2, half, ZoneCount, ZoneDivision);
		second.Reverse();
		return [.. first, .. second];
	}

	/// <summary>One surface's profile from −half to half: the zones in turn by their boundaries (<see cref="LensPart.ZoneBound"/>), each sampled along its arc from its own start at the vertex plane, so a zone's last point and the next zone's first make the step.</summary>
	private static List<(double X, double Y)> Profile(double vertex, double radius, double half, int zones, ZoneDivision division)
	{
		int steps = Math.Max(2, 32 / zones);
		var points = new List<(double X, double Y)>(2 * zones * (steps + 1));
		for (int k = -zones; k < zones; k++)
		{
			int zone = k < 0 ? -k - 1 : k;
			double inner = ZoneBound(radius, half, zones, division, zone);
			double outer = ZoneBound(radius, half, zones, division, zone + 1);
			double shift = SurfaceSag(radius, inner);
			// the negative side runs from the zone's outer edge in to its inner one
			(double from, double to) = k < 0 ? (-outer, -inner) : (inner, outer);
			for (int i = 0; i <= steps; i++)
			{
				double y = from + (to - from) * i / steps;
				points.Add((vertex + SurfaceSag(radius, y) - shift, y));
			}
		}
		return points;
	}

	/// <summary>The zones and the division first, so the fit of the radii reckons with the clamped count.</summary>
	public override void Normalize()
	{
		Zones = Math.Clamp(Zones, 1, MaxZones);
		Division = ZoneDivision;
		base.Normalize();
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<FresnelLensPart>();
}

/// <summary>How a <see cref="FresnelLensPart"/>'s zones are divided over the radius (2026-11-07, the user's request for both).</summary>
public enum ZoneDivision
{
	/// <summary>Every zone the same width from the axis to the rim; the outer zone is the deepest.</summary>
	EqualWidth,
	/// <summary>Every zone the same depth, the rim's sag shared out equally, so the zones narrow outward: the elements of a lighthouse lens.</summary>
	EqualDepth,
}

/// <summary>
/// A part that is a plain shape of glass or mirror: the shared material (<see cref="Mirror"/>, <see cref="RefractiveIndex"/>,
/// the coating of <see cref="CoatedPart"/>, <see cref="Absorption"/> over <see cref="AbsorptionRamp"/>) over an outline each subclass
/// generates (<see cref="Vertices"/>): a regular <see cref="PolygonPart"/>, a <see cref="RectanglePart"/>, an
/// <see cref="EllipsePart"/>, a <see cref="TrianglePart"/>, a free <see cref="OutlinePart"/> (one class each, chosen when
/// the part is added: the user's point, 2026-10-18, against a shape mode inside one class).
/// </summary>
public abstract class ShapePart : CoatedPart
{
	/// <summary>Default <see cref="RefractiveIndex"/>.</summary>
	public const double DefaultIndex = 1.5;

	private bool _mirror;
	private double _refractiveIndex = DefaultIndex;
	private int _absorption;
	private int _absorptionRamp = DefaultAbsorptionRamp;

	/// <summary>Whether the part is a mirror (red 0, a wall no wave gets through) instead of glass; the index, the coating and the absorption are ignored then.</summary>
	[XmlAttribute]
	[DefaultValue(false)]
	[Category("Material")]
	[Description("Make the part a mirror instead of glass. The refractive index, the coating and the absorption are ignored then.")]
	public bool Mirror
	{
		get => _mirror;
		set => SetProperty(ref _mirror, value);
	}

	/// <summary>Refractive index of the glass, 1.00 to 2.55 in steps of 0.01; not used by a mirror.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultIndex)]
	[Range(MinIndex, MaxIndex)]
	[Category("Material")]
	[DisplayName("Refractive index")]
	[Description("Refractive index of the glass, 1.00 to 2.55 in steps of 0.01.")]
	public double RefractiveIndex
	{
		get => _refractiveIndex;
		set => SetProperty(ref _refractiveIndex, value);
	}

	/// <summary>
	/// How strongly the part absorbs, 0 (not at all) to 255, painted into the scene's blue channel (the engines damp a
	/// cell's velocity by 1 − blue / 255 each step) where it rises over <see cref="AbsorptionRamp"/>; a mirror ignores it.
	/// </summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Range(0, MaxAbsorption)]
	[Category("Material")]
	[Description("How strongly the part absorbs waves, 0 (not at all) to 255, as the scene image's blue channel. It rises from nothing at the outline over the absorption ramp, so a wave enters the part instead of being reflected. A mirror ignores it.")]
	public int Absorption
	{
		get => _absorption;
		set => SetProperty(ref _absorption, value);
	}

	/// <summary>Depth in pixels over which the absorption rises from 0 at the outline to <see cref="Absorption"/>; at most half the part's thickness takes effect.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultAbsorptionRamp)]
	[Range(1, MaxAbsorptionRamp)]
	[Category("Material")]
	[DisplayName("Absorption ramp")]
	[Description("Depth in pixels over which the absorption rises from nothing at the outline to its full value; the longer, the less of a wave is reflected. A part thinner than twice the ramp reaches its full absorption in its middle.")]
	public int AbsorptionRamp
	{
		get => _absorptionRamp;
		set => SetProperty(ref _absorptionRamp, value);
	}

	/// <summary>The outline's vertices relative to the centre, before rotation, in canvas pixels; fewer than three paint nothing. Out-of-range values are guarded, not written back.</summary>
	public abstract (double X, double Y)[] Vertices();

	/// <inheritdoc/>
	public override double AxisReach => Vertices().Select(v => Math.Abs(v.X)).DefaultIfEmpty(0.0).Max();

	/// <inheritdoc/>
	public override (double X, double Y)[] Outline() => Vertices();

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		RefractiveIndex = NormalizeIndex(RefractiveIndex);
		Absorption = Math.Clamp(Absorption, 0, MaxAbsorption);
		AbsorptionRamp = Math.Clamp(AbsorptionRamp, 1, MaxAbsorptionRamp);
	}
}

/// <summary>
/// A regular polygon of <see cref="Sides"/> sides of <see cref="Side"/> pixels, a face centred on -x' (toward a source on
/// the left); for an odd count the first vertex lies on +x'.
/// </summary>
public sealed class PolygonPart : ShapePart
{
	/// <summary>Default <see cref="Sides"/>.</summary>
	public const int DefaultSides = 6;
	/// <summary>Smallest <see cref="Sides"/>.</summary>
	public const int MinSides = 3;
	/// <summary>Largest <see cref="Sides"/>.</summary>
	public const int MaxSides = 64;
	/// <summary>Default <see cref="Side"/> in pixels.</summary>
	public const int DefaultSide = 60;

	private int _sides = DefaultSides;
	private int _side = DefaultSide;

	/// <inheritdoc/>
	public override string NameStem => "Polygon";

	/// <summary>Number of sides, 3 to 64.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultSides)]
	[Range(MinSides, MaxSides)]
	[Category("Shape")]
	[DisplayName("Number of sides")]
	[Description("Number of sides, 3 to 64.")]
	public int Sides
	{
		get => _sides;
		set => SetProperty(ref _sides, value);
	}

	/// <summary>Length of each side in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultSide)]
	[Range(2, int.MaxValue)]
	[Category("Shape")]
	[DisplayName("Side length")]
	[Description("Length of each side in pixels.")]
	public int Side
	{
		get => _side;
		set => SetProperty(ref _side, value);
	}

	/// <summary>Circumradius in pixels: side / (2 sin(π / sides)).</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double Circumradius => Math.Max(2, Side) / (2.0 * Math.Sin(Math.PI / Math.Clamp(Sides, MinSides, MaxSides)));

	/// <inheritdoc/>
	public override (double X, double Y)[] Vertices()
	{
		int sides = Math.Clamp(Sides, MinSides, MaxSides);
		double radius = Circumradius;
		var vertices = new (double X, double Y)[sides];
		for (int k = 0; k < sides; k++)
		{
			double theta = Math.PI + (2 * k + 1) * Math.PI / sides;
			vertices[k] = (radius * Math.Cos(theta), radius * Math.Sin(theta));
		}
		return vertices;
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Sides = Math.Clamp(Sides, MinSides, MaxSides);
		Side = Math.Max(2, Side);
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<PolygonPart>();
}

/// <summary>A rectangle of <see cref="Length"/> along the axis and <see cref="Width"/> across, centred. A flat mirror is a long, narrow one.</summary>
public sealed class RectanglePart : ShapePart
{
	/// <summary>Default <see cref="Length"/> and <see cref="Width"/> in pixels.</summary>
	public const int DefaultSize = 60;

	private int _length = DefaultSize;
	private int _width = DefaultSize;
	private bool _linked;

	/// <inheritdoc/>
	public override string NameStem => "Rectangle";

	/// <summary>Extent along the axis in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultSize)]
	[Range(1, int.MaxValue)]
	[Category("Shape")]
	[Description("Extent along the axis in pixels.")]
	public int Length
	{
		get => _length;
		set
		{
			if (SetProperty(ref _length, value) && Linked)
			{
				Width = value;
			}
		}
	}

	/// <summary>Extent across the axis in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultSize)]
	[Range(1, int.MaxValue)]
	[Category("Shape")]
	[Description("Extent across the axis in pixels.")]
	public int Width
	{
		get => _width;
		set
		{
			if (SetProperty(ref _width, value) && Linked)
			{
				Length = value;
			}
		}
	}

	/// <summary>Keeps <see cref="Width"/> equal to <see cref="Length"/> (a square): a change of either sets the other, and the size grip moves both (2026-10-20, the user's request: the chain between width and height of the image editors).</summary>
	[XmlAttribute]
	[DefaultValue(false)]
	[Category("Shape")]
	[DisplayName("Linked")]
	[Description("Keep the width equal to the length.")]
	public bool Linked
	{
		get => _linked;
		set
		{
			if (SetProperty(ref _linked, value) && value)
			{
				Width = Length;
			}
		}
	}

	/// <inheritdoc/>
	public override (double X, double Y)[] Vertices()
	{
		double l = Math.Max(1, Length) / 2.0;
		double w = Math.Max(1, Width) / 2.0;
		return [(-l, -w), (l, -w), (l, w), (-l, w)];
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Length = Math.Max(1, Length);
		Width = Math.Max(1, Width);
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<RectanglePart>();
}

/// <summary>
/// An ellipse of <see cref="Length"/> along the axis and <see cref="Width"/> across, centred; a circle when the two are
/// equal, which <see cref="Linked"/> keeps (2026-10-20, the user's request). Painted and outlined as a polygon of enough vertices for the chord error to
/// stay far below a pixel: a vertex every three pixels of circumference, 24 at least, 180 at most.
/// </summary>
public sealed class EllipsePart : ShapePart
{
	/// <summary>Default <see cref="Length"/> and <see cref="Width"/> in pixels.</summary>
	public const int DefaultSize = 60;

	/// <summary>Fewest vertices of the outline.</summary>
	public const int MinVertices = 24;

	/// <summary>Most vertices of the outline: a 1000 px circle still has a chord sag under 0.2 px.</summary>
	public const int MaxVertices = 180;

	private int _length = DefaultSize;
	private int _width = DefaultSize;
	private bool _linked;

	/// <inheritdoc/>
	public override string NameStem => "Ellipse";

	/// <summary>Diameter along the axis in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultSize)]
	[Range(1, int.MaxValue)]
	[Category("Shape")]
	[Description("Diameter along the axis in pixels.")]
	public int Length
	{
		get => _length;
		set
		{
			if (SetProperty(ref _length, value) && Linked)
			{
				Width = value;
			}
		}
	}

	/// <summary>Diameter across the axis in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultSize)]
	[Range(1, int.MaxValue)]
	[Category("Shape")]
	[Description("Diameter across the axis in pixels.")]
	public int Width
	{
		get => _width;
		set
		{
			if (SetProperty(ref _width, value) && Linked)
			{
				Length = value;
			}
		}
	}

	/// <summary>Keeps <see cref="Width"/> equal to <see cref="Length"/> (a circle): a change of either sets the other, and the size grip moves both (2026-10-20, the user's request: the chain between width and height of the image editors).</summary>
	[XmlAttribute]
	[DefaultValue(false)]
	[Category("Shape")]
	[DisplayName("Linked")]
	[Description("Keep the width equal to the length.")]
	public bool Linked
	{
		get => _linked;
		set
		{
			if (SetProperty(ref _linked, value) && value)
			{
				Width = Length;
			}
		}
	}

	/// <summary>How many vertices the outline gets: one per three pixels of circumference (Ramanujan's estimate), within the limits.</summary>
	public int VertexCount
	{
		get
		{
			double a = Math.Max(1, Length) / 2.0;
			double b = Math.Max(1, Width) / 2.0;
			double h = (a - b) * (a - b) / ((a + b) * (a + b));
			double circumference = Math.PI * (a + b) * (1 + 3 * h / (10 + Math.Sqrt(4 - 3 * h)));
			return Math.Clamp((int)Math.Round(circumference / 3.0), MinVertices, MaxVertices);
		}
	}

	/// <inheritdoc/>
	public override (double X, double Y)[] Vertices()
	{
		double a = Math.Max(1, Length) / 2.0;
		double b = Math.Max(1, Width) / 2.0;
		int n = VertexCount;
		var vertices = new (double X, double Y)[n];
		for (int k = 0; k < n; k++)
		{
			double theta = 2.0 * Math.PI * k / n;
			vertices[k] = (a * Math.Cos(theta), b * Math.Sin(theta));
		}
		return vertices;
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Length = Math.Max(1, Length);
		Width = Math.Max(1, Width);
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<EllipsePart>();
}

/// <summary>The presets of a <see cref="TrianglePart"/>: which base angles are fixed.</summary>
public enum TriangleType
{
	/// <summary>Both base angles 60°.</summary>
	Equilateral,
	/// <summary>Both base angles 45°: the right angle at the apex (the total-internal-reflection prism).</summary>
	RightIsosceles,
	/// <summary>Base angles 90° and 30°: the right angle at the first end of the base.</summary>
	ThirtySixtyNinety,
	/// <summary>Both base angles the same, typed once.</summary>
	Isosceles,
	/// <summary>Both base angles typed.</summary>
	Custom,
}

/// <summary>
/// A triangle on a <see cref="Base"/> across the axis (the face toward a source on the left, at -x'), the apex pointing
/// along +x', shaped by its two base angles <see cref="AngleA"/> (at the -y' end) and <see cref="AngleB"/> (at the +y'
/// end); the apex angle is what remains of 180°. A <see cref="Type"/> preset fixes the angles.
/// </summary>
public sealed class TrianglePart : ShapePart
{
	/// <summary>Default <see cref="Base"/> in pixels.</summary>
	public const int DefaultBase = 60;
	/// <summary>Largest base angle; the two must leave something for the apex.</summary>
	public const double MaxAngle = 178.0;

	private int _base = DefaultBase;
	private TriangleType _type = TriangleType.Equilateral;
	private double _angleA = 60.0;
	private double _angleB = 60.0;

	/// <inheritdoc/>
	public override string NameStem => "Triangle";

	/// <summary>Length of the base in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultBase)]
	[Range(2, int.MaxValue)]
	[Category("Shape")]
	[Description("Length of the base in pixels; the base lies across the axis, toward a source on the left.")]
	public int Base
	{
		get => _base;
		set => SetProperty(ref _base, value);
	}

	/// <summary>The preset: equilateral, right isosceles (45°), 30-60-90, isosceles with one typed angle, or custom with both typed. Choosing one sets the angles.</summary>
	[XmlAttribute]
	[DefaultValue(TriangleType.Equilateral)]
	[Category("Shape")]
	[Description("Equilateral, right isosceles (the 45° prism), 30-60-90, isosceles with one typed angle, or custom with both typed.")]
	public TriangleType Type
	{
		get => _type;
		set
		{
			if (SetProperty(ref _type, value))
			{
				ApplyPreset();
			}
		}
	}

	/// <summary>Base angle at the first end of the base (-y'), in degrees; typed for the isosceles and custom types.</summary>
	[XmlAttribute]
	[DefaultValue(60.0)]
	[Range(1.0, MaxAngle)]
	[Category("Shape")]
	[DisplayName("Angle A")]
	[Description("Base angle at the first end of the base, in degrees. Typed for the isosceles and custom types; the presets fix it.")]
	public double AngleA
	{
		get => _angleA;
		set
		{
			if (SetProperty(ref _angleA, value) && Type == TriangleType.Isosceles)
			{
				AngleB = value;
			}
		}
	}

	/// <summary>Base angle at the second end of the base (+y'), in degrees; typed for the custom type only.</summary>
	[XmlAttribute]
	[DefaultValue(60.0)]
	[Range(1.0, MaxAngle)]
	[Category("Shape")]
	[DisplayName("Angle B")]
	[Description("Base angle at the second end of the base, in degrees. Typed for the custom type; the others fix it.")]
	public double AngleB
	{
		get => _angleB;
		set => SetProperty(ref _angleB, value);
	}

	/// <summary>The angle at the apex in degrees: 180 minus the two base angles (as the painter uses them).</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double ApexAngle
	{
		get
		{
			(double a, double b) = EffectiveAngles();
			return 180.0 - a - b;
		}
	}

	/// <summary>The base angles the type gives, in degrees, within range and leaving at least 2° for the apex; the typed values for the custom type.</summary>
	public (double A, double B) EffectiveAngles()
	{
		double a = double.IsFinite(AngleA) ? Math.Clamp(AngleA, 1.0, MaxAngle) : 60.0;
		double b = double.IsFinite(AngleB) ? Math.Clamp(AngleB, 1.0, MaxAngle) : 60.0;
		switch (Type)
		{
			case TriangleType.Equilateral:
				return (60.0, 60.0);
			case TriangleType.RightIsosceles:
				return (45.0, 45.0);
			case TriangleType.ThirtySixtyNinety:
				return (90.0, 30.0);
			case TriangleType.Isosceles:
				a = Math.Min(a, 89.0);
				return (a, a);
			default:
				return (a, Math.Min(b, MaxAngle + 1.0 - a));
		}
	}

	/// <summary>Writes the type's angles into <see cref="AngleA"/> and <see cref="AngleB"/>.</summary>
	private void ApplyPreset()
	{
		(double a, double b) = EffectiveAngles();
		AngleA = a;
		AngleB = b;
	}

	/// <inheritdoc/>
	public override (double X, double Y)[] Vertices()
	{
		(double a, double b) = EffectiveAngles();
		double alpha = a * Math.PI / 180.0;
		double beta = b * Math.PI / 180.0;
		double half = Math.Max(2, Base) / 2.0;
		// the height from the law of sines, the apex over the base at h / tan α from the first end
		double height = 2.0 * half * Math.Sin(alpha) * Math.Sin(beta) / Math.Sin(alpha + beta);
		double apexY = -half + height * Math.Cos(alpha) / Math.Sin(alpha);
		return [(-height / 2.0, -half), (-height / 2.0, half), (height / 2.0, apexY)];
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Base = Math.Max(2, Base);
		ApplyPreset();
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<TrianglePart>();
}

/// <summary>A free outline from <see cref="Points"/>, relative to the centre before rotation; fewer than three points paint nothing.</summary>
public sealed class OutlinePart : ShapePart
{
	private ObservableCollection<PartPoint> _points = [];

	/// <inheritdoc/>
	public override string NameStem => "Outline";

	/// <summary>The outline's vertices relative to the centre, before rotation; fewer than three points paint nothing.</summary>
	[XmlElement("Point")]
	[Category("Shape")]
	[DisplayName("Points")]
	[Description("The outline's vertices relative to the centre, before rotation. Fewer than three points paint nothing.")]
	public ObservableCollection<PartPoint> Points
	{
		get => _points;
		set => SetProperty(ref _points, value);
	}

	/// <inheritdoc/>
	public override (double X, double Y)[] Vertices() => Points.Select(p => ((double)p.X, (double)p.Y)).ToArray();

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Points ??= [];
	}

	/// <inheritdoc/>
	public override OpticalPart Clone()
	{
		var copy = ShallowClone<OutlinePart>();
		copy.Points = ClonePoints(Points);
		return copy;
	}
}

/// <summary>
/// A diffusor (the user's request, 2026-11-07): a rectangular plate of <see cref="Length"/> along the axis and
/// <see cref="Width"/> across whose front face (−x', toward a source on the left) is rough and whose back is flat; the
/// material, the coating and the absorption of a <see cref="ShapePart"/>. The roughness is a sum of sinusoids across the
/// face: one at the <see cref="CenterWavelength"/> with the <see cref="Amplitude"/>, and about it wavelengths spread by
/// <see cref="Sigma"/>, each weighted by a Gaussian of its distance from the centre wavelength (<see cref="Components"/>);
/// the phases are random, drawn from the project's seed and the part's name (<see cref="ISeeded"/>, as a source's jitter
/// is), so a file reproduces its surface. The profile is computed once and kept until a value it depends on changes
/// (<see cref="Profile"/>). Coatings on the front follow the rough face; the sides are the two short ends.
/// </summary>
public sealed class DiffusorPart : ShapePart, ISeeded, IProfiledPlate
{
	/// <summary>Default <see cref="Length"/> in pixels.</summary>
	public const int DefaultLength = 50;
	/// <summary>Default <see cref="Width"/> in pixels.</summary>
	public const int DefaultWidth = 500;
	/// <summary>Default <see cref="CenterWavelength"/> in pixels.</summary>
	public const double DefaultWavelength = 16.0;
	/// <summary>Shortest <see cref="CenterWavelength"/> in pixels: two pixels, what the profile's one-pixel sampling can hold.</summary>
	public const double MinWavelength = 2.0;
	/// <summary>Longest <see cref="CenterWavelength"/>, <see cref="Amplitude"/> and <see cref="Sigma"/> in pixels.</summary>
	public const double MaxPixels = 10000.0;
	/// <summary>Default <see cref="Amplitude"/> in pixels.</summary>
	public const double DefaultAmplitude = 1.0;
	/// <summary>Default <see cref="Sigma"/> in pixels.</summary>
	public const double DefaultSigma = 6.0;
	/// <summary>
	/// How far the spread reaches on either side of the centre wavelength, in sigmas, and how many wavelengths lie in each
	/// sigma: components at every half sigma out to three sigmas, 13 in all, the outermost at e^(−4.5) of the centre's amplitude.
	/// </summary>
	public const int SpreadSigmas = 3;
	/// <summary>Components per sigma of the spread (see <see cref="SpreadSigmas"/>).</summary>
	public const int StepsPerSigma = 2;
	/// <summary>Stream selector XOR-ed onto the name hash, so the phases come from another <see cref="Pcg32"/> stream than a source's jitter would.</summary>
	private const ulong RoughnessStream = 0x44494646UL;

	private int _length = DefaultLength;
	private int _width = DefaultWidth;
	private double _centerWavelength = DefaultWavelength;
	private double _amplitude = DefaultAmplitude;
	private double _sigma = DefaultSigma;
	private int _projectSeed;
	private double[]? _profile;
	private (int Width, double Wavelength, double Amplitude, double Sigma, string Name, int Seed) _profileKey;

	/// <summary>A diffusor at the defaults; the readouts follow a rename too, as the phases come from the name (the setter is the base class's).</summary>
	public DiffusorPart()
	{
		PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(Name))
			{
				OnRoughnessChanged();
			}
		};
	}

	/// <inheritdoc/>
	public override string NameStem => "Diffusor";

	/// <summary>
	/// A new diffusor coats its flat back alone (2026-11-07, the user's defaults): the faces' defaults are the
	/// <see cref="CoatedPart"/>'s, every face, so a loaded diffusor keeps what its file says and a new one gets these.
	/// </summary>
	public override void PrepareNew()
	{
		FrontCoated = false;
		BackCoated = true;
		SidesCoated = false;
	}

	/// <summary>Extent along the axis in pixels: the plate's thickness at the mean of its rough face.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultLength)]
	[Range(1, int.MaxValue)]
	[Category("Shape")]
	[Description("Extent along the axis in pixels: the plate's thickness at the mean of its rough face.")]
	public int Length
	{
		get => _length;
		set => SetProperty(ref _length, value);
	}

	/// <summary>Extent across the axis in pixels: the rough face's width.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultWidth)]
	[Range(2, int.MaxValue)]
	[Category("Shape")]
	[Description("Extent across the axis in pixels: the width of the rough face.")]
	public int Width
	{
		get => _width;
		set
		{
			if (SetProperty(ref _width, value))
			{
				OnRoughnessChanged();
			}
		}
	}

	/// <summary>The spatial wavelength in pixels at the middle of the roughness's spectrum, the one with the full <see cref="Amplitude"/>.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultWavelength)]
	[Range(MinWavelength, MaxPixels)]
	[Category("Roughness")]
	[DisplayName("Centre wavelength")]
	[Description("The spatial wavelength in pixels at the middle of the roughness's spectrum: the ripple along the face that has the full amplitude. Two pixels at least.")]
	public double CenterWavelength
	{
		get => _centerWavelength;
		set
		{
			if (SetProperty(ref _centerWavelength, value))
			{
				OnRoughnessChanged();
			}
		}
	}

	/// <summary>The amplitude in pixels of the ripple at the centre wavelength; the other wavelengths get less, by their distance from it, and the face carries their sum.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultAmplitude)]
	[Range(0.0, MaxPixels)]
	[Category("Roughness")]
	[Description("The amplitude in pixels of the ripple at the centre wavelength. The other wavelengths of the spread get less, by their distance from the centre, and the face carries the sum of them all; 0 makes the face flat.")]
	public double Amplitude
	{
		get => _amplitude;
		set
		{
			if (SetProperty(ref _amplitude, value))
			{
				OnRoughnessChanged();
			}
		}
	}

	/// <summary>
	/// The spread of the roughness's wavelengths about the centre wavelength, in pixels: a wavelength λ gets the amplitude
	/// times e^(−(λ − centre)² / 2σ²), the bell. 0 leaves the centre wavelength alone, a single ripple; the larger, the more
	/// wavelengths join in and the less regular the face.
	/// </summary>
	[XmlAttribute]
	[DefaultValue(DefaultSigma)]
	[Range(0.0, MaxPixels)]
	[Category("Roughness")]
	[DisplayName("Sigma")]
	[Description("The spread of the roughness's wavelengths about the centre wavelength, in pixels: a wavelength gets the amplitude times the bell e^(−(λ − centre)² / 2σ²). 0 leaves the centre wavelength alone, a single regular ripple; the larger, the more wavelengths join in and the less regular the face.")]
	public double Sigma
	{
		get => _sigma;
		set
		{
			if (SetProperty(ref _sigma, value))
			{
				OnRoughnessChanged();
			}
		}
	}

	/// <inheritdoc/>
	[XmlIgnore]
	[Browsable(false)]
	public int ProjectSeed
	{
		get => _projectSeed;
		set
		{
			if (SetProperty(ref _projectSeed, value))
			{
				OnRoughnessChanged();
			}
		}
	}

	/// <summary>
	/// The wavelengths of the roughness and their amplitudes, in pixels: the centre wavelength at the full amplitude and,
	/// when <see cref="Sigma"/> is above 0, <see cref="SpreadSigmas"/> × <see cref="StepsPerSigma"/> more on either side
	/// of it, a half sigma apart, each at the amplitude times e^(−(λ − centre)² / 2σ²); a wavelength below
	/// <see cref="MinWavelength"/> is left out. Out-of-range values are guarded as the painter guards them. The phases are
	/// drawn per component in this order, so a change of the width or the sigma keeps the phases of the components that stay.
	/// </summary>
	public (double Wavelength, double Amplitude, int Index)[] Components()
	{
		double centre = double.IsFinite(CenterWavelength) ? Math.Clamp(CenterWavelength, MinWavelength, MaxPixels) : DefaultWavelength;
		double amplitude = double.IsFinite(Amplitude) ? Math.Clamp(Amplitude, 0.0, MaxPixels) : 0.0;
		double sigma = double.IsFinite(Sigma) ? Math.Clamp(Sigma, 0.0, MaxPixels) : 0.0;
		if (sigma <= 0.0)
		{
			return [(centre, amplitude, 0)];
		}
		var components = new List<(double, double, int)>();
		int reach = SpreadSigmas * StepsPerSigma;
		for (int k = -reach; k <= reach; k++)
		{
			double wavelength = centre + k * sigma / StepsPerSigma;
			if (wavelength < MinWavelength)
			{
				continue;
			}
			double distance = wavelength - centre;
			components.Add((wavelength, amplitude * Math.Exp(-distance * distance / (2.0 * sigma * sigma)), k));
		}
		return [.. components];
	}

	/// <summary>
	/// The rough face's displacement from the plate's mean front plane in pixels, at every whole pixel across the face from
	/// y' = −width / 2 to +width / 2 (<see cref="Width"/> + 1 values, positive into the plate): the sum of the
	/// <see cref="Components"/>, each a sine of 2π y' / λ with a random phase from the project's seed and the part's name
	/// (one <see cref="Pcg32"/> stream per part, the phases drawn in component order). Computed once and kept until the
	/// width, a roughness value, the name or the seed changes (the user's rule, 2026-11-07); the array is never modified,
	/// so a clone may share it. Any thread for a part no other thread edits.
	/// </summary>
	[XmlIgnore]
	[Browsable(false)]
	public double[] Profile
	{
		get
		{
			int width = Math.Max(2, Width);
			var key = (width, CenterWavelength, Amplitude, Sigma, Name ?? "", ProjectSeed);
			if (_profile == null || !_profileKey.Equals(key))
			{
				_profile = BuildProfile(width, key.Item5, ProjectSeed, Components());
				_profileKey = key;
			}
			return _profile;
		}
	}

	/// <summary>The displacement of each whole pixel across a face of <paramref name="width"/> pixels from the sum of <paramref name="components"/>, the phases from <paramref name="seed"/> and <paramref name="name"/>.</summary>
	private static double[] BuildProfile(int width, string name, int seed, (double Wavelength, double Amplitude, int Index)[] components)
	{
		// the phases: one draw per possible component in order, so a component keeps its phase whether its neighbours are there or not
		var rng = new Pcg32((ulong)(uint)seed, Pcg32.StableHash(name) ^ RoughnessStream);
		int reach = SpreadSigmas * StepsPerSigma;
		var phases = new double[2 * reach + 1];
		for (int k = 0; k < phases.Length; k++)
		{
			phases[k] = 2.0 * Math.PI * rng.NextDouble();
		}
		var profile = new double[width + 1];
		double half = width / 2.0;
		foreach ((double wavelength, double amplitude, int index) in components)
		{
			double phase = phases[index + reach];
			double k = 2.0 * Math.PI / wavelength;
			for (int i = 0; i <= width; i++)
			{
				profile[i] += amplitude * Math.Sin(k * (i - half) + phase);
			}
		}
		return profile;
	}

	/// <summary>The rough face from its highest point to its lowest, in pixels; what the form shows for the roughness as a whole.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double ProfileHeight
	{
		get
		{
			double[] profile = Profile;
			return profile.Max() - profile.Min();
		}
	}

	/// <summary>How many wavelengths make up the roughness (<see cref="Components"/>).</summary>
	[XmlIgnore]
	[Browsable(false)]
	public int ComponentCount => Components().Length;

	/// <summary>Tells the form that the values derived from the roughness follow an edit; the profile itself is rebuilt when next asked for.</summary>
	private void OnRoughnessChanged()
	{
		OnPropertyChanged(nameof(ProfileHeight));
		OnPropertyChanged(nameof(ComponentCount));
	}

	/// <summary>
	/// The front face's x' at every whole pixel across the plate, from y' = −width / 2 to +width / 2: −length / 2 plus the
	/// <see cref="Profile"/>, held at least a pixel before the back face, so the plate is never cut through.
	/// </summary>
	public double[] FrontFace()
	{
		double[] profile = Profile;
		double half = Math.Max(1, Length) / 2.0;
		var front = new double[profile.Length];
		for (int i = 0; i < front.Length; i++)
		{
			front[i] = Math.Min(-half + profile[i], half - 1.0);
		}
		return front;
	}

	/// <inheritdoc/>
	public override double AxisReach => Math.Max(1, Length) / 2.0 + Math.Max(0.0, -Profile.Min());

	/// <summary>The outline: down the rough front face, pixel by pixel, then the two back corners.</summary>
	public override (double X, double Y)[] Vertices()
	{
		double[] front = FrontFace();
		double halfWidth = Math.Max(2, Width) / 2.0;
		double halfLength = Math.Max(1, Length) / 2.0;
		var vertices = new (double X, double Y)[front.Length + 2];
		for (int i = 0; i < front.Length; i++)
		{
			vertices[i] = (front[i], -halfWidth + i);
		}
		vertices[front.Length] = (halfLength, halfWidth);
		vertices[front.Length + 1] = (halfLength, -halfWidth);
		return vertices;
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Length = Math.Max(1, Length);
		Width = Math.Max(2, Width);
		CenterWavelength = double.IsFinite(CenterWavelength) ? Math.Clamp(CenterWavelength, MinWavelength, MaxPixels) : DefaultWavelength;
		Amplitude = double.IsFinite(Amplitude) ? Math.Clamp(Amplitude, 0.0, MaxPixels) : DefaultAmplitude;
		Sigma = double.IsFinite(Sigma) ? Math.Clamp(Sigma, 0.0, MaxPixels) : DefaultSigma;
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<DiffusorPart>();
}

/// <summary>
/// A plate whose front face (−x') is a profile over y', one sample per pixel across the width (a <see cref="DiffusorPart"/>'s
/// roughness, a <see cref="FacettedGratingPart"/>'s facets): what the shared shape (<c>DiffusorShape</c>) paints.
/// </summary>
public interface IProfiledPlate
{
	/// <summary>Extent along the axis in pixels: the plate's thickness at the mean of its front face.</summary>
	int Length { get; }

	/// <summary>Extent across the axis in pixels: the width of the front face.</summary>
	int Width { get; }

	/// <summary>The front face's x' at every whole pixel across the face, from −width / 2 to width / 2 (width + 1 values), each at least a pixel before the back.</summary>
	double[] FrontFace();
}

/// <summary>The facet pattern of a <see cref="FacettedGratingPart"/> (2026-11-08, the user's request for these three, sawtooth the default).</summary>
public enum FacetPattern
{
	/// <summary>A ramp over each pitch from the crest to the trough, then a step back: a blazed grating.</summary>
	Sawtooth,
	/// <summary>A crest over half of each pitch and a trough over the other half: a binary grating.</summary>
	SquareWave,
	/// <summary>A sine over each pitch: a holographic grating.</summary>
	Sinusoidal,
}

/// <summary>
/// A facetted grating (2026-11-08, the user's request, in the Diffraction gratings submenu): a plate of
/// <see cref="Length"/> along the axis and <see cref="Width"/> across, the material of a <see cref="ShapePart"/>, whose
/// front face (−x') carries a periodic relief, <see cref="Pattern"/> repeated every <see cref="Pitch"/> pixels across
/// the face with <see cref="Amplitude"/> pixels either side of the mean; the back is flat. A diffusor with one
/// wavelength, as the user put it, and painted by the same shape: the face is a sample per pixel, so a square wave's
/// and a sawtooth's steps stand between two pixels. The AR coating lies on the back alone (the user's rule; only
/// <see cref="CoatedPart.BackCoated"/> counts, the form hides the other two boxes).
/// </summary>
public sealed class FacettedGratingPart : ShapePart, IProfiledPlate
{
	/// <summary>Default <see cref="Length"/> in pixels.</summary>
	public const int DefaultLength = 50;
	/// <summary>Default <see cref="Width"/> in pixels.</summary>
	public const int DefaultWidth = 500;
	/// <summary>Default <see cref="Pitch"/> in pixels (the user's choice, 2026-11-08, after a first look at 16).</summary>
	public const double DefaultPitch = 32.0;
	/// <summary>Smallest <see cref="Pitch"/> in pixels: the face is sampled a pixel apart.</summary>
	public const double MinPitch = 2.0;
	/// <summary>Default <see cref="Amplitude"/> in pixels (the user's choice, 2026-11-08, after a first look at 2).</summary>
	public const double DefaultAmplitude = 4.0;

	private int _length = DefaultLength;
	private int _width = DefaultWidth;
	private FacetPattern _pattern = FacetPattern.Sawtooth;
	private double _pitch = DefaultPitch;
	private double _amplitude = DefaultAmplitude;

	/// <inheritdoc/>
	public override string NameStem => "Facetted grating";

	/// <summary>Extent along the axis in pixels: the plate's thickness at the mean of its facetted face.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultLength)]
	[Range(1, int.MaxValue)]
	[Category("Shape")]
	[Description("Extent along the axis in pixels: the plate's thickness at the mean of its facetted face.")]
	public int Length
	{
		get => _length;
		set => SetProperty(ref _length, value);
	}

	/// <summary>Extent across the axis in pixels: the width of the facetted face.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultWidth)]
	[Range(2, int.MaxValue)]
	[Category("Shape")]
	[Description("Extent across the axis in pixels: the width of the facetted face.")]
	public int Width
	{
		get => _width;
		set => SetProperty(ref _width, value);
	}

	/// <summary>The facets' shape over each pitch.</summary>
	[XmlAttribute]
	[DefaultValue(FacetPattern.Sawtooth)]
	[Category("Facets")]
	[Description("The facets' shape over each pitch: a sawtooth, a ramp then a step; a square wave, a crest and a trough of half a pitch each; or a sinusoid.")]
	public FacetPattern Pattern
	{
		get => _pattern;
		set => SetProperty(ref _pattern, value);
	}

	/// <summary>The period of the facets across the face in pixels, 2 to <see cref="DiffusorPart.MaxPixels"/>.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultPitch)]
	[Range(MinPitch, DiffusorPart.MaxPixels)]
	[Category("Facets")]
	[Description("The period of the facets across the face in pixels: the grating's pitch, which with the wavelength sets the diffraction angles. Two pixels at least.")]
	public double Pitch
	{
		get => _pitch;
		set => SetProperty(ref _pitch, value);
	}

	/// <summary>The relief's height either side of the mean face in pixels, 0 to <see cref="DiffusorPart.MaxPixels"/>; the facets span twice this.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultAmplitude)]
	[Range(0.0, DiffusorPart.MaxPixels)]
	[Category("Facets")]
	[Description("The relief's height either side of the mean face in pixels: the facets run from this far out to this far in, twice it from crest to trough. 0 makes the face flat.")]
	public double Amplitude
	{
		get => _amplitude;
		set => SetProperty(ref _amplitude, value);
	}

	/// <summary>The relief at every whole pixel across the face, positive into the plate: the pattern over the position in its pitch, from the face's centre.</summary>
	public double[] Profile()
	{
		int width = Math.Max(2, Width);
		double pitch = double.IsFinite(Pitch) ? Math.Clamp(Pitch, MinPitch, DiffusorPart.MaxPixels) : DefaultPitch;
		double amplitude = double.IsFinite(Amplitude) ? Math.Clamp(Amplitude, 0.0, DiffusorPart.MaxPixels) : 0.0;
		FacetPattern pattern = Enum.IsDefined(Pattern) ? Pattern : FacetPattern.Sawtooth;
		var profile = new double[width + 1];
		double half = width / 2.0;
		for (int i = 0; i <= width; i++)
		{
			double t = (i - half) / pitch;
			t -= Math.Floor(t);
			profile[i] = amplitude * pattern switch
			{
				FacetPattern.SquareWave => t < 0.5 ? -1.0 : 1.0,
				FacetPattern.Sinusoidal => Math.Sin(2.0 * Math.PI * t),
				_ => 2.0 * t - 1.0,
			};
		}
		return profile;
	}

	/// <inheritdoc/>
	public double[] FrontFace()
	{
		double[] profile = Profile();
		double half = Math.Max(1, Length) / 2.0;
		var front = new double[profile.Length];
		for (int i = 0; i < front.Length; i++)
		{
			front[i] = Math.Min(-half + profile[i], half - 1.0);
		}
		return front;
	}

	/// <inheritdoc/>
	public override double AxisReach => Math.Max(1, Length) / 2.0 + Math.Max(0.0, -Profile().Min());

	/// <summary>The polygon of the face, a vertex per pixel, then the back's two corners.</summary>
	public override (double X, double Y)[] Vertices()
	{
		double[] front = FrontFace();
		double halfWidth = Math.Max(2, Width) / 2.0;
		double halfLength = Math.Max(1, Length) / 2.0;
		var vertices = new (double X, double Y)[front.Length + 2];
		for (int i = 0; i < front.Length; i++)
		{
			vertices[i] = (front[i], -halfWidth + i);
		}
		vertices[front.Length] = (halfLength, halfWidth);
		vertices[front.Length + 1] = (halfLength, -halfWidth);
		return vertices;
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Length = Math.Max(1, Length);
		Width = Math.Max(2, Width);
		Pattern = Enum.IsDefined(Pattern) ? Pattern : FacetPattern.Sawtooth;
		Pitch = double.IsFinite(Pitch) ? Math.Clamp(Pitch, MinPitch, DiffusorPart.MaxPixels) : DefaultPitch;
		Amplitude = double.IsFinite(Amplitude) ? Math.Clamp(Amplitude, 0.0, DiffusorPart.MaxPixels) : DefaultAmplitude;
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<FacettedGratingPart>();
}

/// <summary>
/// A meta surface (2026-11-08, the user's request): a carrier plate of <see cref="Length"/> along the axis and
/// <see cref="Width"/> across, the material of a <see cref="ShapePart"/>, with a sub-wavelength pattern on its front
/// (−x'): a ridge of <see cref="PatternIndex"/> in every cell of <see cref="Pitch"/> across the face, <see cref="Height"/>
/// deep in front of the plate (the user's choice: before the plate, as the filter's layers), its width the cell's fill
/// factor times the pitch. What the surface does is the phase delay a subclass asks of each cell (<see cref="Delay"/>,
/// wrapped to a turn): a ridge of effective index n_eff delays by (2π/λ)·H·(n_eff − 1), where a cell filled to f has
/// n_eff² = f·n_p² + (1 − f) (the lamellar effective medium), so f = (n_eff² − 1) / (n_p² − 1), clamped to 0..1: a full
/// turn needs a height of λ / (n_p − 1) (<see cref="FullTurnHeight"/>), and a lower pattern saturates. λ comes from
/// <see cref="DesignFrequency"/> through the simulation's frequency scale, as a zone plate's. The AR band lies on the
/// ridges' tops (front) and the plate's back (the user's rule); the plate's sides are not offered.
/// </summary>
public abstract class MetaSurfacePart : ShapePart, IFrequencyDesigned
{
	/// <summary>Default <see cref="Length"/> in pixels.</summary>
	public const int DefaultLength = 50;
	/// <summary>Default <see cref="Width"/> in pixels.</summary>
	public const int DefaultWidth = 500;
	/// <summary>Default <see cref="DesignFrequency"/>: level 50, a wavelength of 32 px at the default solver settings, as the zone plate's.</summary>
	public const double DefaultFrequency = 50.0;
	/// <summary>Default <see cref="Pitch"/> in pixels: an eighth of the default wavelength, sub-wavelength.</summary>
	public const double DefaultPitch = 4.0;
	/// <summary>Default <see cref="Height"/> in pixels: a full turn at the default wavelength and pattern index, λ / (n − 1) = 32.</summary>
	public const double DefaultHeight = 32.0;
	/// <summary>Default <see cref="PatternIndex"/> (the user's choice, 2026-11-08).</summary>
	public const double DefaultPatternIndex = 2.0;

	private int _length = DefaultLength;
	private int _width = DefaultWidth;
	private double _designFrequency = DefaultFrequency;
	private double _frequencyScale = Simulation.DefaultSourceFrequencyScale;
	private double _pitch = DefaultPitch;
	private double _height = DefaultHeight;
	private double _patternIndex = DefaultPatternIndex;

	/// <summary>A new meta surface coats nothing on its sides: the band goes on the ridges' tops and the plate's back when a thickness is set.</summary>
	public override void PrepareNew() => SidesCoated = false;

	/// <summary>Extent of the carrier plate along the axis in pixels, without the pattern.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultLength)]
	[Range(1, int.MaxValue)]
	[Category("Shape")]
	[Description("Thickness of the carrier plate along the axis in pixels; the pattern stands on its front, outside it.")]
	public int Length
	{
		get => _length;
		set => SetProperty(ref _length, value);
	}

	/// <summary>Extent of the plate and the pattern across the axis in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultWidth)]
	[Range(2, int.MaxValue)]
	[Category("Shape")]
	[Description("Width of the plate and its pattern across the axis in pixels.")]
	public int Width
	{
		get => _width;
		set
		{
			if (SetProperty(ref _width, value))
			{
				OnDesignChanged();
			}
		}
	}

	/// <summary>The source frequency the surface is designed for, on a source's scale; the wavelength it gives sets every cell's fill.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultFrequency)]
	[Range(LogicGatePart.MinFrequency, Source.MaxLevel)]
	[Category("Design")]
	[DisplayName("Target frequency")]
	[Description("The source frequency the surface is designed for, on the same scale as a source's frequency. The cells' fill follows from the wavelength it gives; other frequencies see another surface.")]
	public double DesignFrequency
	{
		get => _designFrequency;
		set
		{
			if (SetProperty(ref _designFrequency, value))
			{
				OnDesignChanged();
			}
		}
	}

	/// <inheritdoc/>
	[XmlIgnore]
	[Browsable(false)]
	public double FrequencyScale
	{
		get => _frequencyScale;
		set
		{
			if (SetProperty(ref _frequencyScale, value))
			{
				OnDesignChanged();
			}
		}
	}

	/// <summary>The cell's width across the face in pixels, 1 to <see cref="DiffusorPart.MaxPixels"/>: one ridge per cell; well below the wavelength, so the wave sees a mean index.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultPitch)]
	[Range(1.0, DiffusorPart.MaxPixels)]
	[Category("Pattern")]
	[Description("The width of a cell of the pattern across the face in pixels, one ridge per cell. Well below the wavelength, so the wave sees each cell as a mean index rather than diffracting at it.")]
	public double Pitch
	{
		get => _pitch;
		set
		{
			if (SetProperty(ref _pitch, value))
			{
				OnDesignChanged();
			}
		}
	}

	/// <summary>The ridges' height in front of the plate in pixels, 1 to <see cref="DiffusorPart.MaxPixels"/>; a full turn of phase needs λ / (n − 1).</summary>
	[XmlAttribute]
	[DefaultValue(DefaultHeight)]
	[Range(1.0, DiffusorPart.MaxPixels)]
	[Category("Pattern")]
	[Description("The height of the ridges in front of the plate in pixels. A full turn of phase takes a height of the wavelength over the pattern index less one; a lower pattern cannot delay enough and its fill saturates.")]
	public double Height
	{
		get => _height;
		set
		{
			if (SetProperty(ref _height, value))
			{
				OnDesignChanged();
			}
		}
	}

	/// <summary>Refractive index of the ridges, 1.00 to 2.55 in steps of 0.01; the plate's is <see cref="ShapePart.RefractiveIndex"/>.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultPatternIndex)]
	[Range(MinIndex, MaxIndex)]
	[Category("Pattern")]
	[DisplayName("Pattern index")]
	[Description("Refractive index of the ridges, 1.00 to 2.55 in steps of 0.01, usually above the plate's. The higher, the lower the pattern can be.")]
	public double PatternIndex
	{
		get => _patternIndex;
		set
		{
			if (SetProperty(ref _patternIndex, value))
			{
				OnDesignChanged();
			}
		}
	}

	/// <summary>The wavelength in pixels in a medium of index 1 that <see cref="DesignFrequency"/> gives with <see cref="FrequencyScale"/>; NaN for none.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double Wavelength => Simulation.Wavelength(DesignFrequency, 1.0, FrequencyScale);

	/// <summary>The height in pixels at which a fully filled cell delays by a whole turn, λ / (n − 1).</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double FullTurnHeight => Wavelength / Math.Max(1e-9, GuardedPatternIndex - 1.0);

	/// <summary>How many zones, each a turn of phase, the surface has across its face (a lens's from the centre to the rim), the last one cut by the rim; 0 without a wavelength.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public abstract int ZoneCount { get; }

	/// <summary>Number of cells across the face: the width over the pitch, rounded down, 1 at least.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public int CellCount => Math.Max(1, (int)Math.Floor(Math.Max(2, Width) / GuardedPitch));

	/// <summary>The pitch as the pattern uses it: finite, 1 to the width.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double GuardedPitch => double.IsFinite(Pitch) ? Math.Clamp(Pitch, 1.0, Math.Max(2, Width)) : DefaultPitch;

	/// <summary>The height as the pattern uses it: finite, 1 to <see cref="DiffusorPart.MaxPixels"/>.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double GuardedHeight => double.IsFinite(Height) ? Math.Clamp(Height, 1.0, DiffusorPart.MaxPixels) : DefaultHeight;

	/// <summary>The pattern index as the pattern uses it: finite, within the index range.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double GuardedPatternIndex => double.IsFinite(PatternIndex) ? Math.Clamp(PatternIndex, MinIndex, MaxIndex) : DefaultPatternIndex;

	/// <summary>Whether the design has a wavelength to work with.</summary>
	protected bool HasWavelength => double.IsFinite(Wavelength) && Wavelength > 0.0;

	/// <summary>The y' of the centre of cell <paramref name="cell"/> (0 to <see cref="CellCount"/> − 1), the cells laid from the face's one end, centred together on the axis.</summary>
	public double CellCentre(int cell)
	{
		double pitch = GuardedPitch;
		return (cell - (CellCount - 1) / 2.0) * pitch;
	}

	/// <summary>The phase delay the surface asks of the cell at <paramref name="y"/> from the axis, in radians, any value: it is wrapped to a turn for the fill. Only asked with a wavelength.</summary>
	protected abstract double Delay(double y);

	/// <summary><paramref name="phase"/> wrapped into 0 to 2π.</summary>
	protected static double Wrap(double phase) => phase - 2.0 * Math.PI * Math.Floor(phase / (2.0 * Math.PI));

	/// <summary>
	/// The fill factor of every cell, 0 (no ridge) to 1 (the cell solid): the delay the cell needs (<see cref="Delay"/>,
	/// wrapped to a turn, a full turn kept as one, so a converging centre cell is solid rather than empty), turned into the effective index the ridge must give, (delay / 2π)·λ / H + 1, and that into the
	/// fill through the lamellar mean of n², clamped where the height cannot reach it. Empty without a wavelength.
	/// </summary>
	public double[] FillFactors()
	{
		int cells = CellCount;
		var fills = new double[cells];
		if (!HasWavelength)
		{
			return fills;
		}
		double lambda = Wavelength;
		double height = GuardedHeight;
		double n2 = GuardedPatternIndex * GuardedPatternIndex;
		for (int j = 0; j < cells; j++)
		{
			double asked = Delay(CellCentre(j));
			double delay = asked >= 0.0 && asked <= 2.0 * Math.PI ? asked : Wrap(asked);
			double effective = 1.0 + delay / (2.0 * Math.PI) * lambda / height;
			fills[j] = n2 > 1.0 ? Math.Clamp((effective * effective - 1.0) / (n2 - 1.0), 0.0, 1.0) : 0.0;
		}
		return fills;
	}

	/// <summary>Raises the readouts that follow the design: the wavelength, the full-turn height, the zones, the cells; a subclass adds its own.</summary>
	protected virtual void OnDesignChanged()
	{
		OnPropertyChanged(nameof(Wavelength));
		OnPropertyChanged(nameof(FullTurnHeight));
		OnPropertyChanged(nameof(ZoneCount));
		OnPropertyChanged(nameof(CellCount));
	}

	/// <inheritdoc/>
	public override double AxisReach => Math.Max(1, Length) / 2.0 + GuardedHeight;

	/// <summary>The box around the plate and its pattern: the outline the overlay draws.</summary>
	public override (double X, double Y)[] Vertices()
	{
		double back = Math.Max(1, Length) / 2.0;
		double front = -back - GuardedHeight;
		double w = Math.Max(2, Width) / 2.0;
		return [(front, -w), (back, -w), (back, w), (front, w)];
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Length = Math.Max(1, Length);
		Width = Math.Max(2, Width);
		DesignFrequency = double.IsFinite(DesignFrequency) ? Math.Round(Math.Clamp(DesignFrequency, LogicGatePart.MinFrequency, Source.MaxLevel), 2) : DefaultFrequency;
		Pitch = double.IsFinite(Pitch) ? Math.Clamp(Pitch, 1.0, DiffusorPart.MaxPixels) : DefaultPitch;
		Height = GuardedHeight;
		PatternIndex = NormalizeIndex(PatternIndex);
	}
}

/// <summary>What a <see cref="MetaLensPart"/> does to a plane wave: focus it, or spread it as from a focus behind it.</summary>
public enum MetaLensType
{
	/// <summary>Converging: the cells' delay is greatest at the centre and falls outward within each zone, as a convex lens's glass thins.</summary>
	Converging,
	/// <summary>Diverging: the delay grows outward within each zone, as a concave lens's glass thickens.</summary>
	Diverging,
}

/// <summary>
/// A meta lens (2026-11-08, the user's request, the first entry of the Meta surfaces submenu): a <see cref="MetaSurfacePart"/>
/// whose cells delay as a lens would (the user's choice: computed from the wavelength and the <see cref="FocalLength"/>):
/// a cell at y' needs the phase of a lens, (2π/λ)(√(F² + y'²) − F), wrapped to a turn, taken from a full turn down for
/// a converging lens and up from zero for a diverging one (<see cref="LensType"/>).
/// </summary>
public sealed class MetaLensPart : MetaSurfacePart
{
	/// <summary>Default <see cref="FocalLength"/> in pixels (the user's choice, 2026-11-08).</summary>
	public const int DefaultFocalLength = 500;

	private int _focalLength = DefaultFocalLength;
	private MetaLensType _lensType = MetaLensType.Converging;

	/// <inheritdoc/>
	public override string NameStem => "Meta lens";

	/// <summary>Distance in pixels from the lens to its focus along the axis (+x'), for a plane wave arriving along it; a diverging lens spreads as from a focus that far behind it.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultFocalLength)]
	[Range(1, int.MaxValue)]
	[Category("Lens")]
	[DisplayName("Focal length")]
	[Description("Distance in pixels from the lens to its focus, for a plane wave arriving along the axis; a diverging lens spreads the wave as if from a focus that far behind it.")]
	public int FocalLength
	{
		get => _focalLength;
		set
		{
			if (SetProperty(ref _focalLength, value))
			{
				OnDesignChanged();
			}
		}
	}

	/// <summary>Converging or diverging: which way the fill runs within each zone.</summary>
	[XmlAttribute]
	[DefaultValue(MetaLensType.Converging)]
	[Category("Lens")]
	[DisplayName("Lens type")]
	[Description("Converging, the fill greatest at the centre and falling outward within each zone, as a convex lens's glass thins; or diverging, the fill growing outward.")]
	public MetaLensType LensType
	{
		get => _lensType;
		set
		{
			if (SetProperty(ref _lensType, value))
			{
				OnDesignChanged();
			}
		}
	}

	/// <inheritdoc/>
	public override int ZoneCount => HasWavelength ? (int)Math.Floor(LensPhase(Math.Max(2, Width) / 2.0) / (2.0 * Math.PI)) + 1 : 0;

	/// <summary>The lens's phase lead at <paramref name="y"/> from the axis relative to the centre, in radians, (2π/λ)(√(F² + y²) − F): what a converging lens must take away there, a diverging one add; 0 without a wavelength.</summary>
	public double LensPhase(double y)
	{
		if (!HasWavelength)
		{
			return 0.0;
		}
		double f = Math.Max(1, FocalLength);
		return 2.0 * Math.PI / Wavelength * (Math.Sqrt(f * f + y * y) - f);
	}

	/// <summary>A full turn less the wrapped lens phase for a converging lens, the wrapped phase itself for a diverging one.</summary>
	protected override double Delay(double y)
	{
		double wrapped = Wrap(LensPhase(y));
		return LensType == MetaLensType.Diverging ? wrapped : 2.0 * Math.PI - wrapped;
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		FocalLength = Math.Max(1, FocalLength);
		LensType = Enum.IsDefined(LensType) ? LensType : MetaLensType.Converging;
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<MetaLensPart>();
}

/// <summary>
/// A meta prism (2026-11-08, the user's request, the second entry of the Meta surfaces submenu): a <see cref="MetaSurfacePart"/>
/// whose cells delay in a line across the face, (2π/λ)·y'·sin θ wrapped to a turn, so a plane wave arriving along the
/// axis leaves turned by <see cref="Deflection"/> θ, counter-clockwise on the screen for a positive angle, as a blazed
/// grating's first order does; each zone is a turn, λ / sin θ wide (<see cref="ZoneWidth"/>).
/// </summary>
public sealed class MetaPrismPart : MetaSurfacePart
{
	/// <summary>Default <see cref="Deflection"/> in degrees.</summary>
	public const double DefaultDeflection = 20.0;
	/// <summary>Largest magnitude of <see cref="Deflection"/> in degrees: short of grazing.</summary>
	public const double MaxDeflection = 89.0;

	private double _deflection = DefaultDeflection;

	/// <inheritdoc/>
	public override string NameStem => "Meta prism";

	/// <summary>The angle in degrees by which a plane wave arriving along the axis is turned, −<see cref="MaxDeflection"/> to <see cref="MaxDeflection"/>, counter-clockwise on the screen for a positive angle.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultDeflection)]
	[Range(-MaxDeflection, MaxDeflection)]
	[Category("Prism")]
	[Description("The angle in degrees by which a wave arriving along the axis is turned, counter-clockwise on the screen for a positive angle; 0 turns nothing. The steeper, the narrower the zones, which must stay a few cells wide.")]
	public double Deflection
	{
		get => _deflection;
		set
		{
			if (SetProperty(ref _deflection, value))
			{
				OnDesignChanged();
			}
		}
	}

	/// <summary>The sine of the deflection, guarded.</summary>
	private double Sine => Math.Sin((double.IsFinite(Deflection) ? Math.Clamp(Deflection, -MaxDeflection, MaxDeflection) : DefaultDeflection) * Math.PI / 180.0);

	/// <summary>The width of a zone, a full turn of phase, across the face in pixels: λ / sin θ; infinite at no deflection.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double ZoneWidth => Wavelength / Math.Max(1e-12, Math.Abs(Sine));

	/// <inheritdoc/>
	public override int ZoneCount => HasWavelength ? (int)Math.Floor(Math.Max(2, Width) / ZoneWidth) + 1 : 0;

	/// <summary>A line across the face: the delay grows toward −y' (up the screen at 0°) for a positive angle, so the wavefront lags there and the wave turns that way.</summary>
	protected override double Delay(double y) => -2.0 * Math.PI / Wavelength * y * Sine;

	/// <inheritdoc/>
	protected override void OnDesignChanged()
	{
		base.OnDesignChanged();
		OnPropertyChanged(nameof(ZoneWidth));
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Deflection = double.IsFinite(Deflection) ? Math.Round(Math.Clamp(Deflection, -MaxDeflection, MaxDeflection), 2) : DefaultDeflection;
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<MetaPrismPart>();
}

/// <summary>
/// A meta axicon (2026-11-08, the user's request, the third entry of the Meta surfaces submenu): a <see cref="MetaSurfacePart"/>
/// whose cells delay in proportion to the distance from the axis, a full turn less (2π/λ)·|y'|·sin β wrapped to a
/// turn, so the two halves of a plane wave arriving along the axis are turned toward it by <see cref="ConeAngle"/> β and
/// cross along a line behind the surface (a Bessel-like beam) of about half the width over tan β
/// (<see cref="LineLength"/>) rather than at a point; each zone is λ / sin β wide (<see cref="ZoneWidth"/>).
/// </summary>
public sealed class MetaAxiconPart : MetaSurfacePart
{
	/// <summary>Default <see cref="ConeAngle"/> in degrees.</summary>
	public const double DefaultConeAngle = 10.0;
	/// <summary>Smallest <see cref="ConeAngle"/> in degrees: short of no cone.</summary>
	public const double MinConeAngle = 0.1;
	/// <summary>Largest <see cref="ConeAngle"/> in degrees: short of grazing.</summary>
	public const double MaxConeAngle = 89.0;

	private double _coneAngle = DefaultConeAngle;

	/// <inheritdoc/>
	public override string NameStem => "Meta axicon";

	/// <summary>The angle in degrees by which each half of a wave arriving along the axis is turned toward it, <see cref="MinConeAngle"/> to <see cref="MaxConeAngle"/>.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultConeAngle)]
	[Range(MinConeAngle, MaxConeAngle)]
	[Category("Axicon")]
	[DisplayName("Cone angle")]
	[Description("The angle in degrees by which each half of a wave arriving along the axis is turned toward it. The steeper, the shorter the line of focus behind the surface and the narrower the zones, which must stay a few cells wide.")]
	public double ConeAngle
	{
		get => _coneAngle;
		set
		{
			if (SetProperty(ref _coneAngle, value))
			{
				OnDesignChanged();
			}
		}
	}

	/// <summary>The cone angle guarded, in radians.</summary>
	private double Radians => (double.IsFinite(ConeAngle) ? Math.Clamp(ConeAngle, MinConeAngle, MaxConeAngle) : DefaultConeAngle) * Math.PI / 180.0;

	/// <summary>The width of a zone, a full turn of phase, across the face in pixels: λ / sin β.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double ZoneWidth => Wavelength / Math.Sin(Radians);

	/// <summary>How far behind the surface the line of focus reaches, in pixels: the half width over tan β, where the rim's rays cross the axis.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double LineLength => Math.Max(2, Width) / 2.0 / Math.Tan(Radians);

	/// <inheritdoc/>
	public override int ZoneCount => HasWavelength ? (int)Math.Floor(Math.Max(2, Width) / 2.0 / ZoneWidth) + 1 : 0;

	/// <summary>A full turn less the phase a cone needs, (2π/λ)·|y'|·sin β, so the delay is greatest on the axis and falls outward within each zone, as a converging lens's.</summary>
	protected override double Delay(double y) => 2.0 * Math.PI - Wrap(2.0 * Math.PI / Wavelength * Math.Abs(y) * Math.Sin(Radians));

	/// <inheritdoc/>
	protected override void OnDesignChanged()
	{
		base.OnDesignChanged();
		OnPropertyChanged(nameof(ZoneWidth));
		OnPropertyChanged(nameof(LineLength));
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		ConeAngle = double.IsFinite(ConeAngle) ? Math.Round(Math.Clamp(ConeAngle, MinConeAngle, MaxConeAngle), 2) : DefaultConeAngle;
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<MetaAxiconPart>();
}

/// <summary>One layer of an <see cref="InterferenceFilterPart"/>'s stack: a thickness along the axis and an index.</summary>
public sealed class FilterLayer : ObservableObject
{
	/// <summary>Default <see cref="Thickness"/> in pixels.</summary>
	public const double DefaultThickness = 2.0;
	/// <summary>Default <see cref="RefractiveIndex"/>.</summary>
	public const double DefaultIndex = 1.5;
	/// <summary>Largest <see cref="Thickness"/> in pixels.</summary>
	public const double MaxThickness = 1000.0;

	private double _thickness = DefaultThickness;
	private double _refractiveIndex = DefaultIndex;

	/// <summary>Thickness of the layer along the part's axis in pixels, in steps of 0.01; a fractional boundary is a mixed index at its pixel (the painter samples 4 × 4).</summary>
	[XmlAttribute]
	[DefaultValue(DefaultThickness)]
	[Range(0.0, MaxThickness)]
	[Category("Layer")]
	[Description("Thickness of the layer in pixels, in steps of 0.01. A quarter-wave layer is λ / (4 n) pixels thick; a boundary inside a pixel gives that pixel a mixed index.")]
	public double Thickness
	{
		get => _thickness;
		set => SetProperty(ref _thickness, value);
	}

	/// <summary>Refractive index of the layer, 1.00 to 2.55 in steps of 0.01.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultIndex)]
	[Range(OpticalPart.MinIndex, OpticalPart.MaxIndex)]
	[Category("Layer")]
	[DisplayName("Refractive index")]
	[Description("Refractive index of the layer, 1.00 to 2.55 in steps of 0.01.")]
	public double RefractiveIndex
	{
		get => _refractiveIndex;
		set => SetProperty(ref _refractiveIndex, value);
	}

	/// <summary>The thickness guarded as the painter takes it: finite, 0 to <see cref="MaxThickness"/>.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double GuardedThickness => double.IsFinite(Thickness) ? Math.Clamp(Thickness, 0.0, MaxThickness) : 0.0;

	/// <summary>Clamps the thickness into 0..<see cref="MaxThickness"/> at 0.01 and the index into the part range.</summary>
	public void Normalize()
	{
		Thickness = Math.Round(GuardedThickness, 2);
		RefractiveIndex = double.IsFinite(RefractiveIndex) ? Math.Round(Math.Clamp(RefractiveIndex, OpticalPart.MinIndex, OpticalPart.MaxIndex), 2) : DefaultIndex;
	}

	/// <summary>A copy with the same values and no change subscribers.</summary>
	public FilterLayer Clone() => new() { Thickness = Thickness, RefractiveIndex = RefractiveIndex };

	/// <summary>The values, for display.</summary>
	public override string ToString() => $"{Thickness} px, n {RefractiveIndex}";
}

/// <summary>
/// An interference filter (the user's request, 2026-11-07): a carrier plate of <see cref="Length"/> along the axis and
/// <see cref="Width"/> across, the material and absorption of a <see cref="ShapePart"/>, with a stack of
/// <see cref="Layers"/> on its front face (−x', toward a source on the left), outside the plate, so the plate stays as
/// sized and the part grows by the stack (the user's choice). The first layer is the outermost, the one a wave from the
/// left meets first, the last lies on the plate. The part's centre is the plate's. A reflector (<see cref="ShapePart.Mirror"/>)
/// makes the plate a mirror and leaves the layers glass in front of it (the user's choice: a coated mirror). The plate's
/// AR coating lies on its back alone, the face away from the layers (<see cref="CoatedPart.BackCoated"/>; the front and
/// side flags are not used). A new filter gets a quarter-wave stack (<see cref="PrepareNew"/>).
/// </summary>
public sealed class InterferenceFilterPart : ShapePart
{
	/// <summary>Default <see cref="Length"/> in pixels.</summary>
	public const int DefaultLength = 50;
	/// <summary>Default <see cref="Width"/> in pixels.</summary>
	public const int DefaultWidth = 500;
	/// <summary>The wavelength in pixels a new filter's stack is cut for.</summary>
	public const double DefaultStackWavelength = 16.0;
	/// <summary>Layer pairs of a new filter's stack.</summary>
	public const int DefaultPairs = 5;
	/// <summary>The low index of a new filter's stack.</summary>
	public const double DefaultLowIndex = 1.5;
	/// <summary>The high index of a new filter's stack.</summary>
	public const double DefaultHighIndex = 2.0;

	private int _length = DefaultLength;
	private int _width = DefaultWidth;
	private ObservableCollection<FilterLayer> _layers = [];
	private HashSet<FilterLayer> _watched = [];

	/// <summary>A filter without layers: a file's layers or <see cref="PrepareNew"/>'s stack fill it.</summary>
	public InterferenceFilterPart()
	{
		_layers.CollectionChanged += OnLayersChanged;
	}

	/// <inheritdoc/>
	public override string NameStem => "Interference filter";

	/// <summary>Extent of the carrier plate along the axis in pixels, without the stack.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultLength)]
	[Range(1, int.MaxValue)]
	[Category("Shape")]
	[Description("Thickness of the carrier plate along the axis in pixels; the layer stack lies on its front, outside it.")]
	public int Length
	{
		get => _length;
		set => SetProperty(ref _length, value);
	}

	/// <summary>Extent of the plate and the stack across the axis in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultWidth)]
	[Range(1, int.MaxValue)]
	[Category("Shape")]
	[Description("Width of the plate and its layers across the axis in pixels.")]
	public int Width
	{
		get => _width;
		set => SetProperty(ref _width, value);
	}

	/// <summary>The layers on the plate's front, the outermost first; the order is the order a wave from the left meets them.</summary>
	[XmlElement("Layer")]
	[Category("Layers")]
	[Description("The layers on the plate's front, the outermost first. Each has its thickness in pixels and its refractive index.")]
	public ObservableCollection<FilterLayer> Layers
	{
		get => _layers;
		set
		{
			ObservableCollection<FilterLayer> old = _layers;
			if (SetProperty(ref _layers, value))
			{
				if (old != null)
				{
					old.CollectionChanged -= OnLayersChanged;
				}
				foreach (FilterLayer layer in _watched)
				{
					layer.PropertyChanged -= OnLayerChanged;
				}
				_watched.Clear();
				if (value != null)
				{
					value.CollectionChanged += OnLayersChanged;
					foreach (FilterLayer layer in value)
					{
						Watch(layer);
					}
				}
				OnPropertyChanged(nameof(StackThickness));
			}
		}
	}

	private void Watch(FilterLayer layer)
	{
		if (_watched.Add(layer))
		{
			layer.PropertyChanged += OnLayerChanged;
		}
	}

	/// <summary>A layer came or went: the layers are watched for their thickness, which the stack's readout follows.</summary>
	private void OnLayersChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		foreach (FilterLayer layer in _watched.Where(layer => !_layers.Contains(layer)).ToList())
		{
			layer.PropertyChanged -= OnLayerChanged;
			_watched.Remove(layer);
		}
		foreach (FilterLayer layer in _layers)
		{
			Watch(layer);
		}
		OnPropertyChanged(nameof(StackThickness));
	}

	private void OnLayerChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(FilterLayer.Thickness))
		{
			OnPropertyChanged(nameof(StackThickness));
		}
	}

	/// <summary>The stack's thickness in pixels: the sum of the layers' (guarded) thicknesses.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double StackThickness => Layers.Sum(layer => layer.GuardedThickness);

	/// <summary>
	/// A new filter's stack (the user's choices, 2026-11-07): quarter-wave layers for <see cref="DefaultStackWavelength"/>,
	/// λ / (4 n) thick at 0.01, the usual design that begins and ends with the high index: <see cref="DefaultPairs"/> pairs
	/// of high then low index from the outside in, and one more high-index layer on the plate, eleven layers in all: by
	/// thin-film theory about 92 % reflected at the design wavelength on a plate of 1.50 in air, where the earlier ten
	/// with the low index outermost gave about 70 %.
	/// </summary>
	public override void PrepareNew()
	{
		for (int pair = 0; pair < DefaultPairs; pair++)
		{
			Layers.Add(QuarterWave(DefaultHighIndex));
			Layers.Add(QuarterWave(DefaultLowIndex));
		}
		Layers.Add(QuarterWave(DefaultHighIndex));
	}

	/// <summary>A quarter-wave layer of index <paramref name="index"/> for <see cref="DefaultStackWavelength"/>.</summary>
	private static FilterLayer QuarterWave(double index) =>
		new() { RefractiveIndex = index, Thickness = Math.Round(DefaultStackWavelength / (4.0 * index), 2) };

	/// <inheritdoc/>
	public override double AxisReach => Math.Max(1, Length) / 2.0 + StackThickness;

	/// <summary>The whole part's outline, the stack's outer face to the plate's back: a rectangle.</summary>
	public override (double X, double Y)[] Vertices()
	{
		double back = Math.Max(1, Length) / 2.0;
		double front = -back - StackThickness;
		double w = Math.Max(1, Width) / 2.0;
		return [(front, -w), (back, -w), (back, w), (front, w)];
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Length = Math.Max(1, Length);
		Width = Math.Max(1, Width);
		Layers ??= [];
		foreach (FilterLayer layer in Layers)
		{
			layer.Normalize();
		}
	}

	/// <inheritdoc/>
	public override OpticalPart Clone()
	{
		var copy = ShallowClone<InterferenceFilterPart>();
		// the memberwise copy shares the original's set of watched layers: a fresh set, then the new list through the
		// setter, which watches it (the setter's unsubscribing from the shared list removes the copy's handler, never the original's)
		copy._watched = [];
		copy.Layers = new ObservableCollection<FilterLayer>(Layers.Select(layer => layer.Clone()));
		return copy;
	}
}

/// <summary>
/// A simple 2D diffraction grating (the user's request, 2026-11-07): a lattice of round dots, <see cref="Columns"/> along
/// the axis by <see cref="Rows"/> across, <see cref="ColumnSpacing"/> and <see cref="RowSpacing"/> apart, each dot
/// <see cref="Diameter"/> pixels across, at most half the smaller spacing; nothing between the dots and no carrier (the
/// user's choices). The lattice is the composite source's: an odd count keeps the middle dot on the part's position, an
/// even one straddles it. The dots are of one material, that of a <see cref="ShapePart"/> (a mirror, or glass with an
/// index and absorption; a new grating is a mirror, <see cref="PrepareNew"/>); the coating of <see cref="CoatedPart"/>
/// is not used (too small). A new grating is one column of 31 rows, 16 px apart, 2 px dots: a line grating across the
/// axis, which a wave from the left diffracts at.
/// </summary>
public sealed class SimpleGratingPart : ShapePart
{
	/// <summary>Default <see cref="Columns"/>.</summary>
	public const int DefaultColumns = 1;
	/// <summary>Default <see cref="Rows"/>.</summary>
	public const int DefaultRows = 31;
	/// <summary>Default <see cref="ColumnSpacing"/> and <see cref="RowSpacing"/> in pixels.</summary>
	public const int DefaultSpacing = 16;
	/// <summary>Smallest spacing in pixels: room for a dot of a pixel.</summary>
	public const int MinSpacing = 2;
	/// <summary>Default <see cref="Diameter"/> in pixels.</summary>
	public const int DefaultDiameter = 2;

	private int _columns = DefaultColumns;
	private int _rows = DefaultRows;
	private int _columnSpacing = DefaultSpacing;
	private int _rowSpacing = DefaultSpacing;
	private int _diameter = DefaultDiameter;

	/// <inheritdoc/>
	public override string NameStem => "Grating";

	/// <summary>Number of dots along the axis; odd counts keep the middle dot on the part's position.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultColumns)]
	[Range(1, int.MaxValue)]
	[Category("Lattice")]
	[Description("Number of dots along the axis. Odd counts keep the middle dot on the part's position.")]
	public int Columns
	{
		get => _columns;
		set => SetProperty(ref _columns, value);
	}

	/// <summary>Number of dots across the axis.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultRows)]
	[Range(1, int.MaxValue)]
	[Category("Lattice")]
	[Description("Number of dots across the axis. Odd counts keep the middle dot on the part's position.")]
	public int Rows
	{
		get => _rows;
		set => SetProperty(ref _rows, value);
	}

	/// <summary>Distance between neighbouring columns along the axis in pixels; the largest dot is half of the smaller spacing.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultSpacing)]
	[Range(MinSpacing, int.MaxValue)]
	[Category("Lattice")]
	[DisplayName("Column spacing")]
	[Description("Distance between neighbouring columns along the axis in pixels: the grating's period in that direction. The dots can be at most half the smaller spacing across.")]
	public int ColumnSpacing
	{
		get => _columnSpacing;
		set
		{
			if (SetProperty(ref _columnSpacing, value))
			{
				OnPropertyChanged(nameof(MaxDiameter));
			}
		}
	}

	/// <summary>Distance between neighbouring rows across the axis in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultSpacing)]
	[Range(MinSpacing, int.MaxValue)]
	[Category("Lattice")]
	[DisplayName("Row spacing")]
	[Description("Distance between neighbouring rows across the axis in pixels: the grating's period in that direction. The dots can be at most half the smaller spacing across.")]
	public int RowSpacing
	{
		get => _rowSpacing;
		set
		{
			if (SetProperty(ref _rowSpacing, value))
			{
				OnPropertyChanged(nameof(MaxDiameter));
			}
		}
	}

	/// <summary>Diameter of each dot in pixels, 1 to <see cref="MaxDiameter"/> (half the smaller spacing; a bound that depends on the spacings, so no <c>[Range]</c>).</summary>
	[XmlAttribute]
	[DefaultValue(DefaultDiameter)]
	[Category("Lattice")]
	[Description("Diameter of each dot in pixels, at most half the smaller spacing. A dot of 2 pixels on a whole-pixel position covers a square of 2 × 2 pixels; a mirror dot under 2 pixels covers no pixel centre and paints nothing.")]
	public int Diameter
	{
		get => _diameter;
		set => SetProperty(ref _diameter, value);
	}

	/// <summary>The largest <see cref="Diameter"/>: half the smaller spacing, 1 at least (the user's rule, 2026-11-07).</summary>
	[XmlIgnore]
	[Browsable(false)]
	public int MaxDiameter => Math.Max(1, Math.Min(Spacing(ColumnSpacing), Spacing(RowSpacing)) / 2);

	/// <summary>The diameter as the painter takes it: 1 to <see cref="MaxDiameter"/>.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public int GuardedDiameter => Math.Clamp(Diameter, 1, MaxDiameter);

	/// <summary>A spacing guarded to <see cref="MinSpacing"/> or more.</summary>
	public static int Spacing(int spacing) => Math.Max(MinSpacing, spacing);

	/// <summary>The dot centres in the part frame, column by column: (i − (columns − 1) / 2) × column spacing along the axis, the rows likewise across.</summary>
	public (double X, double Y)[] Points()
	{
		int columns = Math.Max(1, Columns), rows = Math.Max(1, Rows);
		double sx = Spacing(ColumnSpacing), sy = Spacing(RowSpacing);
		var points = new (double X, double Y)[columns * rows];
		for (int i = 0; i < columns; i++)
		{
			for (int j = 0; j < rows; j++)
			{
				points[i * rows + j] = ((i - (columns - 1) / 2.0) * sx, (j - (rows - 1) / 2.0) * sy);
			}
		}
		return points;
	}

	/// <summary>A new grating is a mirror: dots that reflect everything, the plainest grating (the user's choice, 2026-11-07).</summary>
	public override void PrepareNew() => Mirror = true;

	/// <summary>The box around the lattice, the dots' radius included: the outline the overlay draws and the reach of the rotation handle.</summary>
	public override (double X, double Y)[] Vertices()
	{
		double r = GuardedDiameter / 2.0;
		double l = (Math.Max(1, Columns) - 1) / 2.0 * Spacing(ColumnSpacing) + r;
		double w = (Math.Max(1, Rows) - 1) / 2.0 * Spacing(RowSpacing) + r;
		return [(-l, -w), (l, -w), (l, w), (-l, w)];
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Columns = Math.Max(1, Columns);
		Rows = Math.Max(1, Rows);
		ColumnSpacing = Spacing(ColumnSpacing);
		RowSpacing = Spacing(RowSpacing);
		Diameter = GuardedDiameter;
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<SimpleGratingPart>();
}

/// <summary>How the two ends of a <see cref="FiberPart"/> are cut (2026-10-22, the user's request; the ends were round from the distance test before).</summary>
public enum FiberEnds
{
	/// <summary>Cut straight across the path's direction at the endpoint, like a cleaved fibre; the coating band follows the end face.</summary>
	Flat,
	/// <summary>Rounded with the fibre's outer radius.</summary>
	Round,
}

/// <summary>
/// One node of a <see cref="FiberPart"/> path (2026-10-22, the user's request: a Bézier path with handles instead of a
/// polyline): the point the path passes through, in canvas pixels relative to the part's centre before rotation, and
/// the two handles of the cubic segments meeting there, as offsets from the node. The in-handle shapes the segment
/// arriving from the previous node, the out-handle the one leaving to the next; a handle of zero length makes its
/// segment straight at this end, two zero handles a sharp corner. While <see cref="Smooth"/> is set, the handles are
/// kept opposite each other through the node: switching it on lines them up at once (<see cref="FiberPart.AlignNode"/>),
/// and afterwards a handle dragged (<see cref="SetHandle"/>) or typed (the part listens to its nodes) turns the other
/// with it, keeping its length. A node added to a part, or loaded from a file, is taken as it is.
/// </summary>
public sealed class FiberNode : ObservableObject
{
	private int _x;
	private int _y;
	private int _inX;
	private int _inY;
	private int _outX;
	private int _outY;
	private bool _smooth = true;

	/// <summary>X relative to the part's centre in canvas pixels, before rotation.</summary>
	[XmlAttribute]
	[Category("Node")]
	[Description("X relative to the part's centre in canvas pixels, before rotation.")]
	public int X
	{
		get => _x;
		set => SetProperty(ref _x, value);
	}

	/// <summary>Y relative to the part's centre in canvas pixels, before rotation.</summary>
	[XmlAttribute]
	[Category("Node")]
	[Description("Y relative to the part's centre in canvas pixels, before rotation.")]
	public int Y
	{
		get => _y;
		set => SetProperty(ref _y, value);
	}

	/// <summary>X of the in-handle relative to the node in canvas pixels, before rotation; it shapes the segment arriving from the previous node.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Category("In handle")]
	[DisplayName("In X")]
	[Description("X of the in-handle relative to the node, in canvas pixels before rotation. It shapes the segment arriving from the previous node; zero makes that segment straight at this end.")]
	public int InX
	{
		get => _inX;
		set => SetProperty(ref _inX, value);
	}

	/// <summary>Y of the in-handle relative to the node in canvas pixels, before rotation.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Category("In handle")]
	[DisplayName("In Y")]
	[Description("Y of the in-handle relative to the node, in canvas pixels before rotation.")]
	public int InY
	{
		get => _inY;
		set => SetProperty(ref _inY, value);
	}

	/// <summary>X of the out-handle relative to the node in canvas pixels, before rotation; it shapes the segment leaving to the next node.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Category("Out handle")]
	[DisplayName("Out X")]
	[Description("X of the out-handle relative to the node, in canvas pixels before rotation. It shapes the segment leaving to the next node; zero makes that segment straight at this end.")]
	public int OutX
	{
		get => _outX;
		set => SetProperty(ref _outX, value);
	}

	/// <summary>Y of the out-handle relative to the node in canvas pixels, before rotation.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Category("Out handle")]
	[DisplayName("Out Y")]
	[Description("Y of the out-handle relative to the node, in canvas pixels before rotation.")]
	public int OutY
	{
		get => _outY;
		set => SetProperty(ref _outY, value);
	}

	/// <summary>Whether the editors keep the two handles opposite each other through the node, so the path runs smoothly through it; off, the node may be a corner.</summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Node")]
	[Description("Keep the two handles opposite each other through the node, so the path runs smoothly through it. Switching it on lines them up at once; afterwards moving or typing one handle turns the other with it, keeping its length. Off, the node may be a corner.")]
	public bool Smooth
	{
		get => _smooth;
		set => SetProperty(ref _smooth, value);
	}

	/// <summary>Set while <see cref="SetHandle"/> or <see cref="FiberPart.AlignNode"/> writes the handles, so the part's listener leaves the mirrored handle alone.</summary>
	internal bool Mirroring { get; set; }

	/// <summary>The in-handle as a point of the part frame: the node plus its offset.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public (double X, double Y) In => (X + InX, Y + InY);

	/// <summary>The out-handle as a point of the part frame: the node plus its offset.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public (double X, double Y) Out => (X + OutX, Y + OutY);

	/// <summary>
	/// Sets the out-handle (or the in-handle) to the offset (<paramref name="dx"/>, <paramref name="dy"/>); with
	/// <see cref="Smooth"/> the other handle is turned to the opposite direction, keeping <paramref name="otherLength"/>
	/// (its own length when null; an editor passes the length from before a drag, so the rounding of each step does
	/// not creep into it). A zero handle leaves the other alone.
	/// </summary>
	/// <param name="outward">True for the out-handle, false for the in-handle.</param>
	/// <param name="dx">The handle's X offset from the node in canvas pixels.</param>
	/// <param name="dy">The handle's Y offset from the node in canvas pixels.</param>
	/// <param name="otherLength">The length the other handle keeps, or null for its current length.</param>
	public void SetHandle(bool outward, int dx, int dy, double? otherLength = null)
	{
		Mirroring = true;
		try
		{
			if (outward)
			{
				OutX = dx;
				OutY = dy;
			}
			else
			{
				InX = dx;
				InY = dy;
			}
			double length = Math.Sqrt((double)dx * dx + (double)dy * dy);
			if (!Smooth || length == 0.0)
			{
				return;
			}
			(int otherX, int otherY) = outward ? (InX, InY) : (OutX, OutY);
			double keep = otherLength ?? Math.Sqrt((double)otherX * otherX + (double)otherY * otherY);
			int mirrorX = (int)Math.Round(-dx / length * keep);
			int mirrorY = (int)Math.Round(-dy / length * keep);
			if (outward)
			{
				InX = mirrorX;
				InY = mirrorY;
			}
			else
			{
				OutX = mirrorX;
				OutY = mirrorY;
			}
		}
		finally
		{
			Mirroring = false;
		}
	}

	/// <summary>The coordinates, for display.</summary>
	public override string ToString() => $"({X}, {Y})";
}

/// <summary>
/// A fibre: a core of <see cref="CoreDiameter"/> along a path of cubic Bézier segments through <see cref="Nodes"/>,
/// wrapped in a cladding of <see cref="CladdingThickness"/>; the ends cut <see cref="Ends"/>. The path is flattened
/// into chords (<see cref="Flatten"/>) for painting and drawing. The angle rotates the whole path about the centre.
/// </summary>
public sealed class FiberPart : CoatedPart
{
	/// <summary>Default <see cref="CoreIndex"/>.</summary>
	public const double DefaultCoreIndex = 1.5;
	/// <summary>Default <see cref="CoreDiameter"/> in pixels.</summary>
	public const int DefaultCoreDiameter = 20;
	/// <summary>Default <see cref="CladdingIndex"/>.</summary>
	public const double DefaultCladdingIndex = 1.45;
	/// <summary>Default <see cref="CladdingThickness"/> in pixels.</summary>
	public const int DefaultCladdingThickness = 10;
	/// <summary>Default <see cref="Ends"/>.</summary>
	public const FiberEnds DefaultEnds = FiberEnds.Flat;
	/// <summary>Default <see cref="AbsorptionRamp"/>, half the absorbing band's width in pixels.</summary>
	public const int DefaultAbsorptionBand = 10;
	/// <summary>Half the default path length in pixels: the path runs from (-100, 0) to (100, 0).</summary>
	public const int DefaultHalfLength = 100;
	/// <summary>How far a flattened chord may leave the curve, in canvas pixels: far below the painter's quarter-pixel samples.</summary>
	public const double FlattenTolerance = 0.05;

	private double _coreIndex = DefaultCoreIndex;
	private int _coreDiameter = DefaultCoreDiameter;
	private double _claddingIndex = DefaultCladdingIndex;
	private int _claddingThickness = DefaultCladdingThickness;
	private int _absorption;
	private int _absorptionRamp = DefaultAbsorptionBand;
	private FiberEnds _ends = DefaultEnds;
	private ObservableCollection<FiberNode> _nodes = [];
	private HashSet<FiberNode> _watched = [];

	/// <summary>A fibre with an empty path: a file's nodes or <see cref="DefaultPath"/> fill it.</summary>
	public FiberPart()
	{
		Watch(_nodes);
	}

	/// <inheritdoc/>
	public override string NameStem => "Fiber";

	/// <summary>A new fibre gets the <see cref="DefaultPath"/>.</summary>
	public override void PrepareNew() => Nodes = DefaultPath();

	/// <summary>Refractive index of the core, 1.00 to 2.55.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultCoreIndex)]
	[Range(MinIndex, MaxIndex)]
	[Category("Core")]
	[DisplayName("Core index")]
	[Description("Refractive index of the core, 1.00 to 2.55 in steps of 0.01.")]
	public double CoreIndex
	{
		get => _coreIndex;
		set => SetProperty(ref _coreIndex, value);
	}

	/// <summary>Diameter of the core in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultCoreDiameter)]
	[Range(2, int.MaxValue)]
	[Category("Core")]
	[DisplayName("Core diameter")]
	[Description("Diameter of the core in pixels.")]
	public int CoreDiameter
	{
		get => _coreDiameter;
		set => SetProperty(ref _coreDiameter, value);
	}

	/// <summary>Refractive index of the cladding, 1.00 to 2.55; below the core's for guiding.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultCladdingIndex)]
	[Range(MinIndex, MaxIndex)]
	[Category("Cladding")]
	[DisplayName("Cladding index")]
	[Description("Refractive index of the cladding, 1.00 to 2.55 in steps of 0.01. Below the core's index, the fibre guides.")]
	public double CladdingIndex
	{
		get => _claddingIndex;
		set => SetProperty(ref _claddingIndex, value);
	}

	/// <summary>Thickness of the cladding around the core in pixels; 0 for a bare core.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultCladdingThickness)]
	[Range(0, int.MaxValue)]
	[Category("Cladding")]
	[DisplayName("Cladding thickness")]
	[Description("Thickness of the cladding around the core in pixels; 0 for a bare core.")]
	public int CladdingThickness
	{
		get => _claddingThickness;
		set => SetProperty(ref _claddingThickness, value);
	}

	/// <summary>
	/// Peak absorption of the cladding's absorbing band, 0 (none) to 255, painted into the blue channel (2026-10-29, the
	/// user's request, after the author's fibre scene, whose fibres run between absorbing strips): a hump in the outer
	/// part of the cladding, rising over <see cref="AbsorptionRamp"/> pixels from nothing, peaking, and falling back to
	/// nothing at the outer edge, so the guided mode's tail in the inner cladding is untouched and stray light is
	/// absorbed from either side without a damping step; a mode stripper. Nothing on a bare core.
	/// </summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Range(0, MaxAbsorption)]
	[Category("Cladding")]
	[DisplayName("Cladding absorption")]
	[Description("Peak absorption of the cladding's absorbing band, 0 (not at all) to 255, as the scene image's blue channel: a hump in the outer part of the cladding, rising over the band width from nothing, peaking, and falling to nothing at the outer edge, so the guided wave's tail stays untouched and stray light is absorbed from either side. Nothing on a bare core.")]
	public int Absorption
	{
		get => _absorption;
		set => SetProperty(ref _absorption, value);
	}

	/// <summary>Half the absorbing band's width in pixels: the absorption rises over this many pixels and falls over as many; a band wider than the cladding is squeezed into it.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultAbsorptionBand)]
	[Range(1, MaxAbsorptionRamp)]
	[Category("Cladding")]
	[DisplayName("Absorption band")]
	[Description("Half the absorbing band's width in pixels: the absorption rises over this many pixels to its peak and falls over as many to the outer edge. A band wider than the cladding is squeezed into it.")]
	public int AbsorptionRamp
	{
		get => _absorptionRamp;
		set => SetProperty(ref _absorptionRamp, value);
	}

	/// <summary>How the two ends are cut: straight across the path, or rounded.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultEnds)]
	[Category("Shape")]
	[DisplayName("Ends")]
	[Description("How the two ends are cut: flat, straight across the path like a cleaved fibre, or rounded with the outer radius. The AR coating follows a flat end face.")]
	public FiberEnds Ends
	{
		get => _ends;
		set => SetProperty(ref _ends, value);
	}

	/// <summary>The path's nodes relative to the centre, before rotation, each with the handles of the segments meeting there; fewer than two nodes paint nothing.</summary>
	[XmlElement("Node")]
	[Category("Shape")]
	[DisplayName("Nodes")]
	[Description("The path's nodes relative to the centre, before rotation, each with the handles of the cubic segments meeting there. Fewer than two nodes paint nothing.")]
	public ObservableCollection<FiberNode> Nodes
	{
		get => _nodes;
		set
		{
			if (ReferenceEquals(_nodes, value))
			{
				return;
			}
			Unwatch(_nodes);
			SetProperty(ref _nodes, value);
			Watch(_nodes);
		}
	}

	/// <summary>Outer radius in pixels: half the core diameter plus the cladding.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double OuterRadius => CoreDiameter / 2.0 + CladdingThickness;

	/// <summary>The farthest any node or handle lies along x' (the curve stays within its handles' hull), plus the outer radius.</summary>
	public override double AxisReach => (Nodes.Count > 0 ? Nodes.Max(n => Math.Max(Math.Abs(n.X), Math.Max(Math.Abs(n.In.X), Math.Abs(n.Out.X)))) : 0) + OuterRadius;

	/// <summary>The path flattened into chords; the core and cladding bands show through the scene outline.</summary>
	public override (double X, double Y)[] Outline() => Flatten(Nodes);

	/// <inheritdoc/>
	public override bool OutlineClosed => false;

	/// <summary>
	/// The default straight path, from (-100, 0) to (100, 0), the handles a third of the way along it so they can be
	/// taken hold of. Given to a new fibre by <see cref="PrepareNew"/>, not by the constructor:
	/// <see cref="XmlSerializer"/> appends a file's nodes to the list it finds, so the constructor's list must be empty.
	/// </summary>
	public static ObservableCollection<FiberNode> DefaultPath()
	{
		int third = (int)Math.Round(2.0 * DefaultHalfLength / 3.0);
		return
		[
			new FiberNode { X = -DefaultHalfLength, Y = 0, OutX = third },
			new FiberNode { X = DefaultHalfLength, Y = 0, InX = -third },
		];
	}

	/// <summary>
	/// The path through <paramref name="nodes"/> as chords, in the part frame: the cubic segment between two nodes
	/// (the node, its out-handle, the next node's in-handle, the next node) is cut into as many equal steps of t as
	/// keep every chord within <see cref="FlattenTolerance"/> of the curve, a straight segment staying one chord;
	/// consecutive equal points are dropped, so no chord has zero length. Fewer than two nodes give an empty path.
	/// Any thread; reads the nodes once.
	/// </summary>
	public static (double X, double Y)[] Flatten(IList<FiberNode> nodes)
	{
		if (nodes.Count < 2)
		{
			return [];
		}
		var path = new List<(double X, double Y)>(nodes.Count * 8) { (nodes[0].X, nodes[0].Y) };
		for (int i = 1; i < nodes.Count; i++)
		{
			FiberNode a = nodes[i - 1];
			FiberNode b = nodes[i];
			double x0 = a.X, y0 = a.Y, x3 = b.X, y3 = b.Y;
			(double x1, double y1) = a.Out;
			(double x2, double y2) = b.In;
			// a segment whose handles lie on the chord between its nodes is straight and stays one chord; otherwise the
			// second derivative of a cubic is bounded by 6 × the larger second difference of its control points, and n
			// equal steps leave a chord error of at most that bound / (8 n²)
			int steps = 1;
			if (DistanceToSegment(x1, y1, x0, y0, x3, y3) > FlattenTolerance || DistanceToSegment(x2, y2, x0, y0, x3, y3) > FlattenTolerance)
			{
				double second = Math.Max(Math.Sqrt(Square(x0 - 2 * x1 + x2) + Square(y0 - 2 * y1 + y2)), Math.Sqrt(Square(x1 - 2 * x2 + x3) + Square(y1 - 2 * y2 + y3)));
				steps = Math.Clamp((int)Math.Ceiling(Math.Sqrt(0.75 * second / FlattenTolerance)), 1, 512);
			}
			for (int k = 1; k <= steps; k++)
			{
				double t = (double)k / steps;
				double u = 1.0 - t;
				double w0 = u * u * u, w1 = 3 * u * u * t, w2 = 3 * u * t * t, w3 = t * t * t;
				(double X, double Y) point = (w0 * x0 + w1 * x1 + w2 * x2 + w3 * x3, w0 * y0 + w1 * y1 + w2 * y2 + w3 * y3);
				if (point != path[^1])
				{
					path.Add(point);
				}
			}
		}
		return path.Count < 2 ? [] : [.. path];
	}

	private static double Square(double v) => v * v;

	/// <summary>
	/// Lines up the handles of <paramref name="node"/> (2026-10-22, the user's choice for what Smooth means): the in-handle
	/// opposite the out-handle, both keeping their lengths; an in-handle of zero length (with a node before) or an
	/// out-handle of zero (with a node after) takes the other's length; a node with no handles at all gets them from its
	/// neighbours, along the line between them, a third of the way to each. A node not in the list, or alone, is left as it is.
	/// </summary>
	public void AlignNode(FiberNode node)
	{
		int i = Nodes.IndexOf(node);
		bool hasPrevious = i > 0;
		bool hasNext = i >= 0 && i < Nodes.Count - 1;
		double outLength = Length(node.OutX, node.OutY);
		double inLength = Length(node.InX, node.InY);
		node.Mirroring = true;
		try
		{
			if (outLength == 0.0 && inLength == 0.0)
			{
				if (!hasPrevious && !hasNext)
				{
					return;
				}
				(double fromX, double fromY) = hasPrevious ? (Nodes[i - 1].X, Nodes[i - 1].Y) : (node.X, node.Y);
				(double toX, double toY) = hasNext ? (Nodes[i + 1].X, Nodes[i + 1].Y) : (node.X, node.Y);
				double span = Length(toX - fromX, toY - fromY);
				if (span == 0.0)
				{
					return;
				}
				double tx = (toX - fromX) / span;
				double ty = (toY - fromY) / span;
				if (hasPrevious)
				{
					double third = Length(node.X - fromX, node.Y - fromY) / 3.0;
					node.InX = (int)Math.Round(-tx * third);
					node.InY = (int)Math.Round(-ty * third);
				}
				if (hasNext)
				{
					double third = Length(toX - node.X, toY - node.Y) / 3.0;
					node.OutX = (int)Math.Round(tx * third);
					node.OutY = (int)Math.Round(ty * third);
				}
			}
			else if (outLength > 0.0)
			{
				if (inLength == 0.0 && hasPrevious)
				{
					inLength = outLength;
				}
				node.InX = (int)Math.Round(-node.OutX / outLength * inLength);
				node.InY = (int)Math.Round(-node.OutY / outLength * inLength);
			}
			else
			{
				if (hasNext)
				{
					outLength = inLength;
				}
				node.OutX = (int)Math.Round(-node.InX / inLength * outLength);
				node.OutY = (int)Math.Round(-node.InY / inLength * outLength);
			}
		}
		finally
		{
			node.Mirroring = false;
		}
	}

	private static double Length(double x, double y) => Math.Sqrt(x * x + y * y);

	/// <summary>Listens to the list and its nodes for the smooth rule (<see cref="OnNodeChanged"/>).</summary>
	private void Watch(ObservableCollection<FiberNode>? nodes)
	{
		if (nodes == null)
		{
			return;
		}
		nodes.CollectionChanged += OnNodesChanged;
		foreach (FiberNode node in nodes)
		{
			WatchNode(node);
		}
	}

	private void Unwatch(ObservableCollection<FiberNode>? nodes)
	{
		if (nodes != null)
		{
			nodes.CollectionChanged -= OnNodesChanged;
		}
		foreach (FiberNode node in _watched)
		{
			node.PropertyChanged -= OnNodeChanged;
		}
		_watched.Clear();
	}

	private void WatchNode(FiberNode node)
	{
		if (_watched.Add(node))
		{
			node.PropertyChanged += OnNodeChanged;
		}
	}

	/// <summary>A node added is listened to from then on (as it is: no alignment on arrival, which keeps a file's nodes as written); a node removed is let go.</summary>
	private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.Action == NotifyCollectionChangedAction.Reset)
		{
			foreach (FiberNode node in _watched)
			{
				node.PropertyChanged -= OnNodeChanged;
			}
			_watched.Clear();
			foreach (FiberNode node in _nodes)
			{
				WatchNode(node);
			}
			return;
		}
		if (e.OldItems != null)
		{
			foreach (FiberNode node in e.OldItems)
			{
				if (_watched.Remove(node))
				{
					node.PropertyChanged -= OnNodeChanged;
				}
			}
		}
		if (e.NewItems != null)
		{
			foreach (FiberNode node in e.NewItems)
			{
				WatchNode(node);
			}
		}
	}

	/// <summary>
	/// The smooth rule: a node switched to smooth gets its handles lined up at once (<see cref="AlignNode"/>), and a handle
	/// of a smooth node set by itself (a typed value) turns the other handle opposite, keeping its length, as a drag does
	/// through <see cref="FiberNode.SetHandle"/>, which flags the node while it writes so its own mirror is left alone.
	/// </summary>
	private void OnNodeChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (sender is not FiberNode node || node.Mirroring || !node.Smooth)
		{
			return;
		}
		switch (e.PropertyName)
		{
			case nameof(FiberNode.Smooth):
				AlignNode(node);
				break;
			case nameof(FiberNode.InX) or nameof(FiberNode.InY):
				node.SetHandle(false, node.InX, node.InY);
				break;
			case nameof(FiberNode.OutX) or nameof(FiberNode.OutY):
				node.SetHandle(true, node.OutX, node.OutY);
				break;
		}
	}

	/// <summary>Distance from (x, y) to the segment a–b.</summary>
	private static double DistanceToSegment(double x, double y, double ax, double ay, double bx, double by)
	{
		double dx = bx - ax;
		double dy = by - ay;
		double length2 = dx * dx + dy * dy;
		double t = length2 > 0.0 ? Math.Clamp(((x - ax) * dx + (y - ay) * dy) / length2, 0.0, 1.0) : 0.0;
		return Math.Sqrt(Square(ax + t * dx - x) + Square(ay + t * dy - y));
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		CoreIndex = NormalizeIndex(CoreIndex);
		CladdingIndex = NormalizeIndex(CladdingIndex);
		CoreDiameter = Math.Max(2, CoreDiameter);
		CladdingThickness = Math.Max(0, CladdingThickness);
		Absorption = Math.Clamp(Absorption, 0, MaxAbsorption);
		AbsorptionRamp = Math.Clamp(AbsorptionRamp, 1, MaxAbsorptionRamp);
		if (!Enum.IsDefined(Ends))
		{
			Ends = DefaultEnds;
		}
		Nodes ??= [];
	}

	/// <inheritdoc/>
	public override OpticalPart Clone()
	{
		var copy = ShallowClone<FiberPart>();
		// the memberwise copy shares the original's set of watched nodes and listens to nothing yet: a fresh set, then
		// the new list through the setter, which subscribes
		copy._watched = [];
		copy.Nodes = new ObservableCollection<FiberNode>(Nodes.Select(n => new FiberNode { X = n.X, Y = n.Y, InX = n.InX, InY = n.InY, OutX = n.OutX, OutY = n.OutY, Smooth = n.Smooth }));
		return copy;
	}
}

/// <summary>
/// A Bragg reflector: <see cref="Pairs"/> pairs of layers of <see cref="Thickness1"/> and <see cref="Thickness2"/>
/// pixels with the indices <see cref="Index1"/> and <see cref="Index2"/>, stacked along +x', the stack centred on
/// the part's centre and <see cref="Width"/> across. Thicknesses are pixels as typed; a quarter-wave layer is
/// λ / (4 n) pixels.
/// </summary>
public sealed class BraggReflectorPart : OpticalPart
{
	/// <summary>Default <see cref="Pairs"/>.</summary>
	public const int DefaultPairs = 10;
	/// <summary>Largest <see cref="Pairs"/>.</summary>
	public const int MaxPairs = 200;
	/// <summary>Default layer thickness in pixels.</summary>
	public const int DefaultThickness = 10;
	/// <summary>Default <see cref="Index1"/>.</summary>
	public const double DefaultIndex1 = 1.5;
	/// <summary>Default <see cref="Index2"/>.</summary>
	public const double DefaultIndex2 = 2.0;
	/// <summary>Default <see cref="Width"/> in pixels.</summary>
	public const int DefaultWidth = 200;

	private int _pairs = DefaultPairs;
	private int _thickness1 = DefaultThickness;
	private int _thickness2 = DefaultThickness;
	private double _index1 = DefaultIndex1;
	private double _index2 = DefaultIndex2;
	private int _width = DefaultWidth;

	/// <inheritdoc/>
	public override string NameStem => "Bragg reflector";

	/// <summary>Number of layer pairs, 1 to 200.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultPairs)]
	[Range(1, MaxPairs)]
	[Category("Layers")]
	[DisplayName("Layer pairs")]
	[Description("Number of layer pairs, 1 to 200.")]
	public int Pairs
	{
		get => _pairs;
		set => SetProperty(ref _pairs, value);
	}

	/// <summary>Thickness of the first layer of each pair in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultThickness)]
	[Range(1, int.MaxValue)]
	[Category("Layers")]
	[DisplayName("Thickness 1")]
	[Description("Thickness of the first layer of each pair in pixels. A quarter-wave layer is λ / (4 n) pixels thick.")]
	public int Thickness1
	{
		get => _thickness1;
		set => SetProperty(ref _thickness1, value);
	}

	/// <summary>Thickness of the second layer of each pair in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultThickness)]
	[Range(1, int.MaxValue)]
	[Category("Layers")]
	[DisplayName("Thickness 2")]
	[Description("Thickness of the second layer of each pair in pixels. A quarter-wave layer is λ / (4 n) pixels thick.")]
	public int Thickness2
	{
		get => _thickness2;
		set => SetProperty(ref _thickness2, value);
	}

	/// <summary>Refractive index of the first layer of each pair, 1.00 to 2.55.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultIndex1)]
	[Range(MinIndex, MaxIndex)]
	[Category("Layers")]
	[DisplayName("Index 1")]
	[Description("Refractive index of the first layer of each pair, 1.00 to 2.55 in steps of 0.01.")]
	public double Index1
	{
		get => _index1;
		set => SetProperty(ref _index1, value);
	}

	/// <summary>Refractive index of the second layer of each pair, 1.00 to 2.55.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultIndex2)]
	[Range(MinIndex, MaxIndex)]
	[Category("Layers")]
	[DisplayName("Index 2")]
	[Description("Refractive index of the second layer of each pair, 1.00 to 2.55 in steps of 0.01.")]
	public double Index2
	{
		get => _index2;
		set => SetProperty(ref _index2, value);
	}

	/// <summary>Extent of the stack across the axis in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultWidth)]
	[Range(2, int.MaxValue)]
	[Category("Shape")]
	[Description("Extent of the stack across the axis in pixels.")]
	public int Width
	{
		get => _width;
		set => SetProperty(ref _width, value);
	}

	/// <summary>Total thickness of the stack along the axis in pixels.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public int Length => Pairs * (Thickness1 + Thickness2);

	/// <inheritdoc/>
	public override double AxisReach => Length / 2.0;

	/// <inheritdoc/>
	public override (double X, double Y)[] Outline()
	{
		double l = Math.Clamp(Pairs, 1, MaxPairs) * (Math.Max(1, Thickness1) + Math.Max(1, Thickness2)) / 2.0;
		double w = Math.Max(1, Width) / 2.0;
		return [(-l, -w), (l, -w), (l, w), (-l, w)];
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Pairs = Math.Clamp(Pairs, 1, MaxPairs);
		Thickness1 = Math.Max(1, Thickness1);
		Thickness2 = Math.Max(1, Thickness2);
		Index1 = NormalizeIndex(Index1);
		Index2 = NormalizeIndex(Index2);
		Width = Math.Max(2, Width);
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<BraggReflectorPart>();
}

/// <summary>
/// A gradient-index lens: a rectangle of <see cref="Length"/> along the axis and <see cref="Diameter"/> across whose
/// index falls from <see cref="CentreIndex"/> on the axis to <see cref="EdgeIndex"/> at the edge as
/// n(y') = n_c − (n_c − n_e)·(2y'/D)².
/// </summary>
public sealed class GrinLensPart : CoatedPart
{
	/// <summary>Default <see cref="Length"/> in pixels.</summary>
	public const int DefaultLength = 100;
	/// <summary>Default <see cref="Diameter"/> in pixels.</summary>
	public const int DefaultDiameter = 100;
	/// <summary>Default <see cref="CentreIndex"/>.</summary>
	public const double DefaultCentreIndex = 1.8;
	/// <summary>Default <see cref="EdgeIndex"/>.</summary>
	public const double DefaultEdgeIndex = 1.5;

	private int _length = DefaultLength;
	private int _diameter = DefaultDiameter;
	private double _centreIndex = DefaultCentreIndex;
	private double _edgeIndex = DefaultEdgeIndex;

	/// <inheritdoc/>
	public override string NameStem => "GRIN lens";

	/// <summary>Extent along the axis in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultLength)]
	[Range(1, int.MaxValue)]
	[Category("Shape")]
	[Description("Extent along the axis in pixels.")]
	public int Length
	{
		get => _length;
		set => SetProperty(ref _length, value);
	}

	/// <summary>Extent across the axis in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultDiameter)]
	[Range(2, int.MaxValue)]
	[Category("Shape")]
	[Description("Extent across the axis in pixels.")]
	public int Diameter
	{
		get => _diameter;
		set => SetProperty(ref _diameter, value);
	}

	/// <summary>Refractive index on the axis, 1.00 to 2.55.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultCentreIndex)]
	[Range(MinIndex, MaxIndex)]
	[Category("Material")]
	[DisplayName("Centre index")]
	[Description("Refractive index on the axis, 1.00 to 2.55 in steps of 0.01.")]
	public double CentreIndex
	{
		get => _centreIndex;
		set => SetProperty(ref _centreIndex, value);
	}

	/// <summary>Refractive index at the edge, 1.00 to 2.55; the profile between is parabolic.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultEdgeIndex)]
	[Range(MinIndex, MaxIndex)]
	[Category("Material")]
	[DisplayName("Edge index")]
	[Description("Refractive index at the edge, 1.00 to 2.55 in steps of 0.01. The profile between the axis and the edge is parabolic.")]
	public double EdgeIndex
	{
		get => _edgeIndex;
		set => SetProperty(ref _edgeIndex, value);
	}

	/// <inheritdoc/>
	public override double AxisReach => Length / 2.0;

	/// <inheritdoc/>
	public override (double X, double Y)[] Outline()
	{
		double l = Math.Max(1, Length) / 2.0;
		double d = Math.Max(1, Diameter) / 2.0;
		return [(-l, -d), (l, -d), (l, d), (-l, d)];
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Length = Math.Max(1, Length);
		Diameter = Math.Max(2, Diameter);
		CentreIndex = NormalizeIndex(CentreIndex);
		EdgeIndex = NormalizeIndex(EdgeIndex);
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<GrinLensPart>();
}

/// <summary>What one output of a <see cref="LogicGatePart"/> gives, in powers relative to one input's for the inputs 00, 10, 01, 11.</summary>
public enum GateOutput
{
	/// <summary>0, ½, ½, 0: exclusive or, the waves cancel when both are on.</summary>
	Xor,
	/// <summary>0, ½, ½, 2: the sum in phase; read as AND with a threshold between ½ and 2 (4 : 1, the most a passive linear gate gives).</summary>
	And,
	/// <summary>0, ½, ½, ½: or, the waves meet at 120° and two give what one gives.</summary>
	Or,
	/// <summary>0, ½, ½, 1½: what the OR gate's coupler sends the other way, the waves meeting at 60°.</summary>
	Remainder,
}

/// <summary>
/// An optical logic gate, the interferometric logic of a Mach-Zehnder's combining half (the user's choice, 2026-10-21: a
/// 2 × 2 directional coupler): two single-mode waveguides, input A above the axis and input B below, a phase section that
/// puts wave B <see cref="PhaseShift"/> ahead of wave A, and a 3 dB coupler whose upper output carries (a + i·b) / √2 and
/// lower output (i·a + b) / √2 of two coherent inputs (equal waves of the same frequency and phase). The phase shift alone
/// makes the gate (the user's request, 2026-10-22, for AND and OR in the same dimensions as the XOR): +90° gives the upper
/// output a − b (<see cref="XorGatePart"/>), −90° a + b (<see cref="AndGatePart"/>), +30° a + e^{i120°}·b
/// (<see cref="OrGatePart"/>); the phase section sits in a stretch of the same length in all three, so they share their
/// dimensions and ports and tile together. The whole layout follows from <see cref="DesignFrequency"/> (through
/// <see cref="Wavelength"/>) and the indices (<see cref="LogicGateDesign"/>): the core width, the cladding, the gap, the
/// coupler and phase-section lengths are computed with the engines' own discrete wave equation (the 4th-order stencil;
/// the 9-point stencil's waves are slower and miss the design). Both ends are open and carry an anti-reflection band
/// (<see cref="Coating"/>) unless switched off per end (<see cref="InputCoated"/>, <see cref="OutputCoated"/>), so light
/// comes in from a fibre or a gate before and leaves into the next, and each output can
/// be switched off by an absorber in its arm (<see cref="UpperOutputActive"/>, <see cref="LowerOutputActive"/>). The gate
/// is passive; its inputs can be ordinary sources placed on <see cref="Ports"/>.
/// </summary>
public abstract class LogicGatePart : OpticalPart, IFrequencyDesigned
{
	/// <summary>Default <see cref="DesignFrequency"/>: level 50, a wavelength of 32 px at the default solver settings, the user's wavelength (2026-10-21); twelve pixels a period in the core, so the grid resolves the guide well.</summary>
	public const double DefaultFrequency = 50.0;
	/// <summary>Lowest <see cref="DesignFrequency"/>.</summary>
	public const double MinFrequency = 1.0;
	/// <summary>Shortest <see cref="Wavelength"/> the design accepts, in pixels.</summary>
	public const double MinWavelength = 6.0;
	/// <summary>Longest <see cref="Wavelength"/> the design accepts, in pixels.</summary>
	public const double MaxWavelength = 200.0;
	/// <summary>
	/// Default <see cref="Coating"/> in pixels, the least reflecting band on the default gate, measured from the standing wave
	/// in an output arm (2026-10-22): 0, 3, 5, 6, 7, 8 and 9 px reflected 24, 16, 5.5, 3.4, 3.9, 7.5 and 11 % of the power.
	/// A little above the quarter wave λ / (4 √n) = 5.0 px of the core, as the mode runs partly in the cladding.
	/// </summary>
	public const int DefaultCoating = 6;
	/// <summary>
	/// Default <see cref="CoreIndex"/>, the largest: the strongest contrast to the cladding holds the mode tightest, so the
	/// bends and the coupler are shortest. Measured at λ 32 (2026-10-21): cores of 2.0, 2.3 and 2.55 made gates of 726, 588
	/// and 539 px whose XOR output kept 1.4, 1.9 and 1.1 % with both inputs on; a core of 1.6 at λ 16 made 1100 px.
	/// </summary>
	public const double DefaultCoreIndex = 2.55;
	/// <summary>Default <see cref="CladdingIndex"/>.</summary>
	public const double DefaultCladdingIndex = 1.45;
	/// <summary>Default <see cref="PortSpacing"/> in pixels: as close as the design allows (it raises any smaller value); 60 px at the defaults, a unit 120 px wide.</summary>
	public const int DefaultPortSpacing = 2;
	/// <summary>Largest <see cref="PortSpacing"/> in pixels.</summary>
	public const int MaxPortSpacing = 2000;

	private double _designFrequency = DefaultFrequency;
	private double _frequencyScale = Simulation.DefaultSourceFrequencyScale;
	private double _coreIndex = DefaultCoreIndex;
	private double _claddingIndex = DefaultCladdingIndex;
	private int _portSpacing = DefaultPortSpacing;
	private int _coating = DefaultCoating;
	private bool _inputCoated = true;
	private bool _outputCoated = true;
	private bool _upperOutputActive = true;
	private bool _lowerOutputActive = true;

	/// <summary>How far wave B leads wave A into the coupler, in degrees; fixed per kind.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public abstract double PhaseShift { get; }

	/// <summary>What the upper output gives; fixed per kind.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public abstract GateOutput UpperOutput { get; }

	/// <summary>What the lower output gives; fixed per kind.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public abstract GateOutput LowerOutput { get; }

	/// <summary>
	/// The source frequency the gate is designed for, as a source level (the scale of <see cref="Source.Frequency"/>): the
	/// frequency of the sources that drive it (the user's point, 2026-10-22: every source input is a frequency, so the
	/// gate takes one too and shows the wavelength). The layout follows from the <see cref="Wavelength"/> it gives.
	/// </summary>
	[XmlAttribute]
	[DefaultValue(DefaultFrequency)]
	[Range(MinFrequency, Source.MaxLevel)]
	[Category("Design")]
	[DisplayName("Design frequency")]
	[Description("The source frequency the gate is designed for, on the same scale as a source's frequency: the frequency of the sources that drive it. The wavelength, and from it the core width, the coupler and the phase section, follow from it, so the gate works at this frequency only.")]
	public double DesignFrequency
	{
		get => _designFrequency;
		set
		{
			if (SetProperty(ref _designFrequency, value))
			{
				OnDesignChanged();
			}
		}
	}

	/// <summary>
	/// The simulation's <see cref="Simulation.SourceFrequencyScale"/>, which turns <see cref="DesignFrequency"/> into an
	/// angular frequency; kept current by the window's <see cref="Parts.GatePortFollower"/>, the default scale until then.
	/// Not stored: the project's simulation settings hold it. UI thread.
	/// </summary>
	[XmlIgnore]
	[Browsable(false)]
	public double FrequencyScale
	{
		get => _frequencyScale;
		set
		{
			if (SetProperty(ref _frequencyScale, value))
			{
				OnDesignChanged();
			}
		}
	}

	/// <summary>
	/// The wavelength in pixels in a medium of index 1 that <see cref="DesignFrequency"/> gives: λ = 2π / ω with
	/// ω = level / 255 × 0.5 × <see cref="FrequencyScale"/> radians per step and the engines' speed of 1 pixel per step at
	/// index 1 (<see cref="Simulation.Wavelength"/> with that speed); NaN for a frequency that gives none.
	/// </summary>
	[XmlIgnore]
	[Browsable(false)]
	public double Wavelength => Simulation.Wavelength(DesignFrequency, 1.0, FrequencyScale);

	/// <summary>Thickness in pixels of the anti-reflection band at the ends <see cref="InputCoated"/> and <see cref="OutputCoated"/> select, with the index √n of the core and of the cladding; 0 for none. A quarter-wave band is λ / (4 √n) thick.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultCoating)]
	[Range(0, MaxCoating)]
	[Category("Material")]
	[DisplayName("AR coating")]
	[Description("Thickness in pixels of the anti-reflection band at the ends ticked below, with the index √n of the core and of the cladding; 0 for none. A quarter-wave band, the least reflecting, is λ / (4 √n) pixels thick; the form shows it for the core.")]
	public int Coating
	{
		get => _coating;
		set => SetProperty(ref _coating, value);
	}

	/// <summary>
	/// Whether the input end carries the anti-reflection band (<see cref="Coating"/>). Off where the gate takes its light
	/// from a gate before it (the user's request, 2026-10-22: a band in the junction keeps the wave from passing from one
	/// unit into the next).
	/// </summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Material")]
	[DisplayName("AR on input side")]
	[Description("Put the anti-reflection band on the input end. Switch it off where the gate takes its light from a gate before it, so the wave passes through the junction.")]
	public bool InputCoated
	{
		get => _inputCoated;
		set => SetProperty(ref _inputCoated, value);
	}

	/// <summary>Whether the output end carries the anti-reflection band (<see cref="Coating"/>); off where the gate hands its light to a gate after it (see <see cref="InputCoated"/>).</summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Material")]
	[DisplayName("AR on output side")]
	[Description("Put the anti-reflection band on the output end. Switch it off where the gate hands its light to a gate after it, so the wave passes through the junction.")]
	public bool OutputCoated
	{
		get => _outputCoated;
		set => SetProperty(ref _outputCoated, value);
	}

	/// <summary>Whether the upper output (<see cref="UpperOutput"/>) lets its light out; switched off, an absorber in the blue channel takes it in the arm's last stretch.</summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Outputs")]
	[DisplayName("Upper output")]
	[Description("Let the upper output's light leave the gate. Switched off, an absorber with a soft rise takes it in the last stretch of the arm.")]
	public bool UpperOutputActive
	{
		get => _upperOutputActive;
		set => SetProperty(ref _upperOutputActive, value);
	}

	/// <summary>Whether the lower output (<see cref="LowerOutput"/>) lets its light out; switched off, an absorber in the blue channel takes it in the arm's last stretch.</summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Outputs")]
	[DisplayName("Lower output")]
	[Description("Let the lower output's light leave the gate. Switched off, an absorber with a soft rise takes it in the last stretch of the arm.")]
	public bool LowerOutputActive
	{
		get => _lowerOutputActive;
		set => SetProperty(ref _lowerOutputActive, value);
	}

	/// <summary>Refractive index of the waveguide cores, 1.00 to 2.55; above the cladding's for guiding.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultCoreIndex)]
	[Range(MinIndex, MaxIndex)]
	[Category("Material")]
	[DisplayName("Core index")]
	[Description("Refractive index of the waveguide cores, 1.00 to 2.55 in steps of 0.01. It must be above the cladding's; the larger the difference, the narrower the cores and the tighter the bends.")]
	public double CoreIndex
	{
		get => _coreIndex;
		set
		{
			if (SetProperty(ref _coreIndex, value))
			{
				OnPropertyChanged(nameof(PortSpacingUsed));
			}
		}
	}

	/// <summary>Refractive index of the cladding around the cores, 1.00 to 2.55.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultCladdingIndex)]
	[Range(MinIndex, MaxIndex)]
	[Category("Material")]
	[DisplayName("Cladding index")]
	[Description("Refractive index of the cladding around the cores, 1.00 to 2.55 in steps of 0.01.")]
	public double CladdingIndex
	{
		get => _claddingIndex;
		set
		{
			if (SetProperty(ref _claddingIndex, value))
			{
				OnPropertyChanged(nameof(PortSpacingUsed));
			}
		}
	}

	/// <summary>Distance between the two inputs (and the two outputs) in pixels, centre to centre, as typed; the design raises a value below what it needs to keep the arms apart (<see cref="PortSpacingUsed"/>).</summary>
	[XmlAttribute]
	[DefaultValue(DefaultPortSpacing)]
	[Range(2, MaxPortSpacing)]
	[Category("Shape")]
	[DisplayName("Port spacing")]
	[Description("Distance between the two inputs, and between the two outputs, in pixels. A value below what the design needs to keep the arms apart is raised to it.")]
	public int PortSpacing
	{
		get => _portSpacing;
		set
		{
			if (SetProperty(ref _portSpacing, value))
			{
				OnPropertyChanged(nameof(PortSpacingUsed));
			}
		}
	}

	/// <summary>
	/// The port spacing the design uses, what the form shows and edits: <see cref="PortSpacing"/> raised to the design's
	/// minimum (60 px at the defaults), so every step of the box moves the ports (the user's report, 2026-10-21: the box
	/// stood at 2 and its steps up to the minimum changed nothing). Setting it sets <see cref="PortSpacing"/>. Not stored.
	/// </summary>
	[XmlIgnore]
	[Browsable(false)]
	public int PortSpacingUsed
	{
		get => Design?.PortSpacing ?? PortSpacing;
		set => PortSpacing = value;
	}

	/// <summary>Tells the form that the values derived from the design follow an edit of a design input.</summary>
	private void OnDesignChanged()
	{
		OnPropertyChanged(nameof(Wavelength));
		OnPropertyChanged(nameof(PortSpacingUsed));
	}

	/// <summary>The layout for the current values (guarded as the painter guards them), or null when the cores guide nothing. Any thread; cached.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public LogicGateDesign? Design => double.IsFinite(Wavelength) && Wavelength > 0.0
		? LogicGateDesign.For(Math.Round(Math.Clamp(Wavelength, MinWavelength, MaxWavelength), 3), NormalizeIndex(CoreIndex), NormalizeIndex(CladdingIndex), Math.Clamp(PortSpacing, 2, MaxPortSpacing), PhaseShift)
		: null;

	/// <inheritdoc/>
	public override double AxisReach => (Design?.Length ?? 0) / 2.0;

	/// <summary>The outer edge of the claddings: along the upper arm's top, back along the lower arm's bottom; a closed outline of the gate's extent.</summary>
	public override (double X, double Y)[] Outline()
	{
		if (Design is not { } design)
		{
			return [];
		}
		const int steps = 64;
		double outer = design.CoreWidth / 2.0 + design.CladdingThickness + design.AbsorberWidth;
		var points = new (double X, double Y)[2 * (steps + 1)];
		for (int i = 0; i <= steps; i++)
		{
			double x = design.Left + (double)design.Length * i / steps;
			double offset = design.ArmOffset(x) + outer;
			points[i] = (x, -offset);
			points[2 * steps + 1 - i] = (x, offset);
		}
		return points;
	}

	/// <summary>The ports in canvas pixels (rounded to the pixel a source would sit on): input A, input B, the upper output and the lower output; null without a design.</summary>
	public (int X, int Y)[]? Ports()
	{
		if (Design is not { } design)
		{
			return null;
		}
		double half = design.PortSpacing / 2.0;
		(int, int) At(double x, double y)
		{
			(double cx, double cy) = ToCanvas(x, y);
			return ((int)Math.Floor(cx), (int)Math.Floor(cy));
		}
		return [At(design.InputX, -half), At(design.InputX, half), At(design.OutputX, -half), At(design.OutputX, half)];
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		DesignFrequency = double.IsFinite(DesignFrequency) ? Math.Round(Math.Clamp(DesignFrequency, MinFrequency, Source.MaxLevel), 2) : DefaultFrequency;
		CoreIndex = NormalizeIndex(CoreIndex);
		CladdingIndex = NormalizeIndex(CladdingIndex);
		PortSpacing = Math.Clamp(PortSpacing, 2, MaxPortSpacing);
		Coating = Math.Clamp(Coating, 0, MaxCoating);
	}
}

/// <summary>
/// An XOR gate: wave B leads by 90°, so the upper output carries (a − b) / √2, dark when both inputs are on, and the lower
/// the in-phase sum (the 2026-10-21 design, measured: with both inputs on the upper output keeps about 1 % of one input's
/// power, the lower 1.9×).
/// </summary>
public sealed class XorGatePart : LogicGatePart
{
	/// <inheritdoc/>
	public override string NameStem => "XOR gate";

	/// <inheritdoc/>
	public override double PhaseShift => 90.0;

	/// <inheritdoc/>
	public override GateOutput UpperOutput => GateOutput.Xor;

	/// <inheritdoc/>
	public override GateOutput LowerOutput => GateOutput.And;

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<XorGatePart>();
}

/// <summary>
/// An AND gate in the only sense a passive linear gate has one: wave A leads by 90°, so the upper output carries the in-phase
/// sum (a + b) / √2, half an input's power for one input and twice it for two, 4 : 1, to be read with a threshold between;
/// the lower output carries the XOR. The XOR gate with its outputs exchanged.
/// </summary>
public sealed class AndGatePart : LogicGatePart
{
	/// <inheritdoc/>
	public override string NameStem => "AND gate";

	/// <inheritdoc/>
	public override double PhaseShift => -90.0;

	/// <inheritdoc/>
	public override GateOutput UpperOutput => GateOutput.And;

	/// <inheritdoc/>
	public override GateOutput LowerOutput => GateOutput.Xor;

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<AndGatePart>();
}

/// <summary>
/// An OR gate: wave B leads by 30°, so the waves meet at 120° in the upper output, a + e^{i120°}·b over √2, and one input or
/// two give the same half an input's power (1 + 1 + 2 cos 120° = 1); the lower output, where they meet at 60°, gives ½, ½ and 1½.
/// </summary>
public sealed class OrGatePart : LogicGatePart
{
	/// <inheritdoc/>
	public override string NameStem => "OR gate";

	/// <inheritdoc/>
	public override double PhaseShift => 30.0;

	/// <inheritdoc/>
	public override GateOutput UpperOutput => GateOutput.Or;

	/// <inheritdoc/>
	public override GateOutput LowerOutput => GateOutput.Remainder;

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<OrGatePart>();
}

/// <summary>
/// A Fresnel zone plate (the user's request, 2026-10-22), a diffractive lens: a plate of <see cref="Thickness"/> along the
/// axis and <see cref="Diameter"/> across it, cut into zones at the radii rₙ = √(n·λ·f + (n·λ/2)²) from the axis, λ the
/// <see cref="Wavelength"/> of <see cref="DesignFrequency"/> and f the <see cref="FocalLength"/>; every second zone is a
/// block that absorbs (the user's choice of material), the central zone open, or a block when <see cref="Inverse"/>. The
/// waves through the open zones arrive at the focus a whole number of periods apart and add there. The blocks are painted
/// into the blue channel only, the refractive index left as it is: full at their middle, rising quadratically over
/// <see cref="Coating"/> from the faces the wave meets and leaves, so they reflect little; their sides toward the
/// neighbouring zones stay sharp, as the outer zones are narrow (about λ·f / <see cref="Diameter"/>). Light passes freely
/// outside the diameter unless the absorbing <see cref="Mount"/> is on. Positions and lengths are canvas pixels.
/// </summary>
public sealed class ZonePlatePart : OpticalPart, IFrequencyDesigned
{
	/// <summary>Default <see cref="Diameter"/> in pixels.</summary>
	public const int DefaultDiameter = 800;
	/// <summary>Default <see cref="Thickness"/> in pixels: a wavelength at the default frequency (the user's choice, 2026-11-08; 64 before, two wavelengths with the rise on both faces, see <see cref="BlockAbsorption"/>).</summary>
	public const int DefaultThickness = 32;
	/// <summary>Default <see cref="Coating"/> in pixels: no rise (the user's choice, 2026-11-08; a wavelength, 32, before, see <see cref="BlockAbsorption"/>).</summary>
	public const int DefaultCoating = 0;
	/// <summary>Default <see cref="DesignFrequency"/>: level 50, a wavelength of 32 px at the default solver settings, as the logic gates.</summary>
	public const double DefaultFrequency = 50.0;
	/// <summary>Default <see cref="FocalLength"/> in pixels.</summary>
	public const int DefaultFocalLength = 600;
	/// <summary>
	/// The absorption (blue) in the middle of a block. Measured on a block of the whole diameter with a plane wave at λ 32
	/// (2026-10-22, transmitted / reflected power): 255 over a 16 px rise in 48 px 0.4 / 10.7 %, over 24 px 0.3 / 4.9 %;
	/// 128 over 24 px 0.3 / 2.2 %; 64 over 24 px 2.7 / 0.9 %; in 64 px over 32 px, 64 1.0 / 0.7 %, 96 0.4 / 1.0 %, 128
	/// 0.3 / 1.2 %. A block that stops all and reflects nothing does not exist: a strong absorber is a wall of its own.
	/// </summary>
	public const int BlockAbsorption = 96;

	/// <summary>Default <see cref="MountWidth"/> in pixels.</summary>
	public const int DefaultMountWidth = 200;

	private int _diameter = DefaultDiameter;
	private int _thickness = DefaultThickness;
	private int _coating = DefaultCoating;
	private double _designFrequency = DefaultFrequency;
	private double _frequencyScale = Simulation.DefaultSourceFrequencyScale;
	private int _focalLength = DefaultFocalLength;
	private bool _inverse;
	private bool _mount;
	private int _mountWidth = DefaultMountWidth;

	/// <inheritdoc/>
	public override string NameStem => "Zone plate";

	/// <summary>
	/// Whether an absorbing mount surrounds the plate (the user's request, 2026-10-22): a band of <see cref="MountWidth"/>
	/// on either side beyond the diameter, the plate's thickness and absorption, so no light passes outside the zones (which
	/// otherwise meets the focused light on the axis and pushes the focus on).
	/// </summary>
	[XmlAttribute]
	[DefaultValue(false)]
	[Category("Shape")]
	[Description("Surround the plate with an absorbing mount, so no light passes outside its diameter. Without it, that light meets the focused light on the axis and moves the focus further out, the more so the fewer zones the plate has.")]
	public bool Mount
	{
		get => _mount;
		set => SetProperty(ref _mount, value);
	}

	/// <summary>Width in pixels of the mount on either side, beyond the diameter.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultMountWidth)]
	[Range(1, int.MaxValue)]
	[Category("Shape")]
	[DisplayName("Mount width")]
	[Description("Width in pixels of the absorbing mount on either side, beyond the diameter.")]
	public int MountWidth
	{
		get => _mountWidth;
		set => SetProperty(ref _mountWidth, value);
	}

	/// <summary>Half the plate's outer extent across the axis in pixels: half the diameter, plus the mount when it is on.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double OuterHalfWidth => Math.Max(2, Diameter) / 2.0 + (Mount ? Math.Max(1, MountWidth) : 0);

	/// <summary>Extent across the axis in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultDiameter)]
	[Range(2, int.MaxValue)]
	[Category("Shape")]
	[Description("Extent across the axis in pixels; the zones fill it.")]
	public int Diameter
	{
		get => _diameter;
		set
		{
			if (SetProperty(ref _diameter, value))
			{
				OnPropertyChanged(nameof(ZoneCount));
			}
		}
	}

	/// <summary>Extent along the axis in pixels, the blocks' depth.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultThickness)]
	[Range(1, int.MaxValue)]
	[Category("Shape")]
	[Description("Extent along the axis in pixels: the depth of the blocks. It should leave room for the anti-reflection rise on both faces.")]
	public int Thickness
	{
		get => _thickness;
		set => SetProperty(ref _thickness, value);
	}

	/// <summary>Depth in pixels over which a block's absorption rises from nothing at the faces the wave meets and leaves to its full strength; at most half the thickness takes effect.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultCoating)]
	[Range(0, MaxAbsorptionRamp)]
	[Category("Shape")]
	[DisplayName("Anti-reflection")]
	[Description("Depth in pixels over which a block's absorption rises from nothing at its faces to its full strength, so the blocks reflect little; at most half the thickness takes effect.")]
	public int Coating
	{
		get => _coating;
		set => SetProperty(ref _coating, value);
	}

	/// <summary>The source frequency the plate is designed for, as a source level (the scale of <see cref="Source.Frequency"/>).</summary>
	[XmlAttribute]
	[DefaultValue(DefaultFrequency)]
	[Range(LogicGatePart.MinFrequency, Source.MaxLevel)]
	[Category("Design")]
	[DisplayName("Target frequency")]
	[Description("The source frequency the plate is designed for, on the same scale as a source's frequency. The zones follow from the wavelength it gives, so the plate focuses this frequency at the focal length; other frequencies focus elsewhere.")]
	public double DesignFrequency
	{
		get => _designFrequency;
		set
		{
			if (SetProperty(ref _designFrequency, value))
			{
				OnDesignChanged();
			}
		}
	}

	/// <inheritdoc/>
	[XmlIgnore]
	[Browsable(false)]
	public double FrequencyScale
	{
		get => _frequencyScale;
		set
		{
			if (SetProperty(ref _frequencyScale, value))
			{
				OnDesignChanged();
			}
		}
	}

	/// <summary>Distance in pixels from the plate's centre to the focus along the axis (+x'), for a plane wave arriving along it.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultFocalLength)]
	[Range(1, int.MaxValue)]
	[Category("Design")]
	[DisplayName("Focal length")]
	[Description("Distance in pixels from the plate to its focus, for a plane wave arriving along the axis.")]
	public int FocalLength
	{
		get => _focalLength;
		set
		{
			if (SetProperty(ref _focalLength, value))
			{
				OnPropertyChanged(nameof(ZoneCount));
			}
		}
	}

	/// <summary>Whether the zones are exchanged: the central zone a block and the first ring open, and so on.</summary>
	[XmlAttribute]
	[DefaultValue(false)]
	[Category("Design")]
	[Description("Exchange the zones: the central zone becomes a block, the first ring open, and so on. The focus stays where it is.")]
	public bool Inverse
	{
		get => _inverse;
		set => SetProperty(ref _inverse, value);
	}

	/// <summary>The wavelength in pixels in a medium of index 1 that <see cref="DesignFrequency"/> gives with <see cref="FrequencyScale"/> (the engines' speed of 1 pixel per step); NaN for none.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double Wavelength => Simulation.Wavelength(DesignFrequency, 1.0, FrequencyScale);

	/// <summary>How many zones, open and blocked, fit within half the diameter (the last one cut by the rim); 0 without a wavelength.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public int ZoneCount
	{
		get
		{
			double half = Math.Max(2, Diameter) / 2.0;
			return ZoneAt(half) is { } zone ? zone + 1 : 0;
		}
	}

	/// <summary>
	/// The zone, counted from 0 at the axis, that the distance <paramref name="r"/> from the axis falls in: the n with
	/// rₙ ≤ r &lt; rₙ₊₁, from rₙ² = n·λ·f + (n·λ/2)², a quadratic in n; null without a wavelength or a focal length.
	/// </summary>
	public int? ZoneAt(double r)
	{
		double lambda = Wavelength;
		double f = Math.Max(1, FocalLength);
		if (!double.IsFinite(lambda) || lambda <= 0.0)
		{
			return null;
		}
		// (λ²/4)·n² + λf·n − r² = 0
		double a = lambda * lambda / 4.0;
		double b = lambda * f;
		double n = (-b + Math.Sqrt(b * b + 4.0 * a * r * r)) / (2.0 * a);
		return (int)Math.Floor(n);
	}

	/// <summary>Whether the distance <paramref name="r"/> from the axis falls in a block: the odd zones, or the even ones when inverse.</summary>
	public bool IsBlockAt(double r) => ZoneAt(Math.Abs(r)) is { } zone && (zone % 2 == 1) != Inverse;

	/// <summary>The width in pixels of the outermost whole zone within the diameter, rₙ − rₙ₋₁; what the grid must resolve.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double OuterZoneWidth
	{
		get
		{
			double lambda = Wavelength;
			int count = ZoneCount;
			if (count < 2)
			{
				return Math.Max(2, Diameter) / 2.0;
			}
			double f = Math.Max(1, FocalLength);
			double Radius(int n) => Math.Sqrt(n * lambda * f + n * lambda / 2.0 * (n * lambda / 2.0));
			return Radius(count - 1) - Radius(count - 2);
		}
	}

	/// <summary>Tells the form that the values derived from the wavelength follow an edit.</summary>
	private void OnDesignChanged()
	{
		OnPropertyChanged(nameof(Wavelength));
		OnPropertyChanged(nameof(ZoneCount));
	}

	/// <inheritdoc/>
	public override double AxisReach => Math.Max(1, Thickness) / 2.0;

	/// <summary>The plate's rectangle, the mount included when it is on.</summary>
	public override (double X, double Y)[] Outline()
	{
		double t = Math.Max(1, Thickness) / 2.0;
		double d = OuterHalfWidth;
		return [(-t, -d), (t, -d), (t, d), (-t, d)];
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		MountWidth = Math.Max(1, MountWidth);
		Diameter = Math.Max(2, Diameter);
		Thickness = Math.Max(1, Thickness);
		Coating = Math.Clamp(Coating, 0, MaxAbsorptionRamp);
		DesignFrequency = double.IsFinite(DesignFrequency) ? Math.Round(Math.Clamp(DesignFrequency, LogicGatePart.MinFrequency, Source.MaxLevel), 2) : DefaultFrequency;
		FocalLength = Math.Max(1, FocalLength);
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<ZonePlatePart>();
}
