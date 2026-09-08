using Godot;
using Miner49er.Core;

namespace Miner49er;

/// <summary>Godot-side view of the C64 palette. The palette itself lives in
/// Miner49er.Core so it can be unit-tested without an engine; this is the thin
/// adapter that turns it into Godot Colors.</summary>
public static class C64Colors
{
	public static Color From(Rgb c) => Color.Color8(c.R, c.G, c.B);

	public static readonly Color Black      = From(C64Palette.Black);
	public static readonly Color White      = From(C64Palette.White);
	public static readonly Color Red        = From(C64Palette.Red);
	public static readonly Color Cyan       = From(C64Palette.Cyan);
	public static readonly Color Purple     = From(C64Palette.Purple);
	public static readonly Color Green      = From(C64Palette.Green);
	public static readonly Color Blue       = From(C64Palette.Blue);
	public static readonly Color Yellow     = From(C64Palette.Yellow);
	public static readonly Color Orange     = From(C64Palette.Orange);
	public static readonly Color Brown      = From(C64Palette.Brown);
	public static readonly Color LightRed   = From(C64Palette.LightRed);
	public static readonly Color DarkGrey   = From(C64Palette.DarkGrey);
	public static readonly Color Grey       = From(C64Palette.Grey);
	public static readonly Color LightGreen = From(C64Palette.LightGreen);
	public static readonly Color LightBlue  = From(C64Palette.LightBlue);
	public static readonly Color LightGrey  = From(C64Palette.LightGrey);
}
