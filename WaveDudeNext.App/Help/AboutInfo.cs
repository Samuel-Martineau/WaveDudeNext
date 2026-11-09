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
//   Mark <mark@pab-ai.com>, author of the Avalonia port
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using WaveDudeNext.Engine;
using WaveDudeNext.Export;
using WaveDudeNext.Runs;

namespace WaveDudeNext.App.Help;

/// <summary>
/// What the About dialog shows and what a bug report needs: the product name and version from the assembly attributes
/// (set in <c>Directory.Build.props</c>), the copyright lines of upstream and of the port, the credits and links of the
/// source header, the licence text embedded from <c>LICENSE.md</c>, and the system facts (runtime, OS, memory, cores,
/// the OpenCL devices, what writes H.264 here). Thread-free; <see cref="SystemReport"/> enumerates the OpenCL devices
/// and looks for ffmpeg, a few milliseconds, call it once.
/// </summary>
public static class AboutInfo
{
	private static readonly Assembly Assembly = typeof(AboutInfo).Assembly;

	/// <summary>The YouTube channel of Huygens Optics.</summary>
	public const string HuygensYouTube = "https://www.youtube.com/@HuygensOptics";

	/// <summary>The site of Huygens Optics.</summary>
	public const string HuygensSite = "https://www.huygensoptics.com";

	/// <summary>The YouTube channel of Diffraction Limited (0x23).</summary>
	public const string DiffractionYouTube = "https://www.youtube.com/@DiffractionLimited";

	/// <summary>The WaveSimulator2D repository.</summary>
	public const string WaveSimulator2D = "https://github.com/0x23/WaveSimulator2D";

	/// <summary>The GNU General Public License, version 3.</summary>
	public const string Gpl = "https://www.gnu.org/licenses/gpl-3.0.html";

	/// <summary>The upstream repository.</summary>
	public const string Upstream = "https://github.com/graealex/WaveDudeNext";

	/// <summary>The first credit line, word for word as in the source header, so every place names the author the same way.</summary>
	public const string CreditVleggaar = "Jeroen Vleggaar (Huygens Optics), author of Wave Dude 2D, Copyright (C) 2026";

	/// <summary>The second credit line, word for word as in the source header.</summary>
	public const string CreditWaveSimulator2D = "0x23 (Diffraction Limited), author of WaveSimulator2D";

	/// <summary>The product name of the assembly ("WaveDude Next").</summary>
	public static string Product { get; } = Assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "WaveDude Next";

	/// <summary>
	/// Upstream's copyright line, as the author's About shows it and his checks read it; the port's own line is
	/// <see cref="PortCopyright"/>. (The assembly's copyright attribute holds both in one line, for the file properties.)
	/// </summary>
	public static string Copyright => "Copyright (C) 2026 Alexander Gräf";

	/// <summary>The port's copyright line, shown under upstream's.</summary>
	public static string PortCopyright => "Linux and Windows port Copyright (C) 2026 JJM Vleggaar (Huygens Optics)";

	/// <summary>
	/// The version for people: "0.1.3 (05e5865)", the informational version with the SDK's commit hash shortened to
	/// seven characters in brackets; without a hash, the plain version.
	/// </summary>
	public static string Version { get; } = FormatVersion(
		Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
		?? Assembly.GetName().Version?.ToString(3) ?? Program.DisplayVersion);

	/// <summary>Splits "0.1.0+05e58652c8..." into "0.1.0 (05e5865)"; a hash of seven characters or fewer stays as it is.</summary>
	public static string FormatVersion(string informational)
	{
		int plus = informational.IndexOf('+');
		if (plus < 0)
		{
			return informational;
		}
		string hash = informational[(plus + 1)..];
		if (hash.Length > 7)
		{
			hash = hash[..7];
		}
		return hash.Length == 0 ? informational[..plus] : $"{informational[..plus]} ({hash})";
	}

	/// <summary>
	/// LICENSE.md as Markdown, embedded by the project file under that name and rendered by
	/// <see cref="MarkdownDocument"/> in the About window; a build without the resource gets one paragraph pointing at
	/// gnu.org.
	/// </summary>
	public static string LicenseMarkdown() =>
		MarkdownDocument.ReadEmbedded("LICENSE.md") ?? "LICENSE.md is not embedded in this build. See " + Gpl;

	/// <summary>
	/// The system facts as "label: value" lines, the labels looked up through <paramref name="label"/> (a string table
	/// key to its text). <paramref name="gpuSetting"/> is the application's GPU switch. The OpenCL devices are enumerated
	/// here; a missing OpenCL is reported as a line, never thrown. The eight lines: program, runtime, operating system,
	/// processor, memory, the GPU setting, the OpenCL devices, the H.264 encoders.
	/// </summary>
	public static string SystemReport(bool gpuSetting, Func<string, string> label)
	{
		var sb = new StringBuilder();
		void Line(string key, string value) => sb.Append(label(key)).Append(": ").AppendLine(value);
		Line("AboutProductLine", $"{Product} {Version}");
		Line("AboutRuntime", $"{RuntimeInformation.FrameworkDescription}, {RuntimeInformation.ProcessArchitecture}");
		Line("AboutOperatingSystem", $"{RuntimeInformation.OSDescription}, {RuntimeInformation.OSArchitecture}");
		Line("AboutProcessor", $"{Environment.ProcessorCount} {label("AboutCores")}");
		Line("AboutMemory", $"{FrameMemory.Gigabytes(FrameMemory.AvailableBytes())} {label("AboutMemoryFree")} {FrameMemory.Gigabytes(FrameMemory.PhysicalBytes())}");
		Line("AboutGpuSetting", label(gpuSetting ? "AboutOn" : "AboutOff"));
		Line("AboutOpenClDevices", Devices(label));
		Line("AboutH264Encoders", Encoders(label));
		return sb.ToString().TrimEnd();
	}

	private static string Devices(Func<string, string> label)
	{
		try
		{
			List<GpuDeviceInfo> devices = GpuDeviceInfo.FindAll();
			return devices.Count == 0
				? label("AboutNone")
				: string.Join("; ", devices.Select(d => $"{d.Name} ({d.Vendor}, {FrameMemory.Gigabytes(d.GlobalMemory)})"));
		}
		catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
		{
			return label("AboutOpenClMissing");
		}
	}

	/// <summary>The H.264 encoders Media Foundation offers, as the WPF head lists them; MP4 off Windows is a question of its own (the port piped to ffmpeg).</summary>
	private static string Encoders(Func<string, string> label)
	{
		if (!OperatingSystem.IsWindows() || !Mp4Writer.IsAvailable())
		{
			return label("AboutMediaFoundationMissing");
		}
		IReadOnlyList<string> encoders = Mp4Writer.AvailableEncoders(hardwareOnly: false);
		return encoders.Count == 0 ? label("AboutNone") : string.Join("; ", encoders);
	}
}
