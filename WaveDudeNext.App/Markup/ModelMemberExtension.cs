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

using System.ComponentModel;
using System.Reflection;
using Avalonia;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.XamlIl.Runtime;

namespace WaveDudeNext.App.Markup;

/// <summary>
/// Base of the markup extensions that read a model property's <see cref="System.ComponentModel"/> metadata, so labels
/// and tooltips in the views come from the one place that defines them (upstream's <c>ModelMemberExtension</c>). The
/// member is written as <c>prefix:Type.Property</c>, resolved with the XAML namespaces in scope. The value is always a
/// binding: a string table entry (<c>Label.Type.Prop</c>, <c>Hint.Type.Prop</c>) wins over the attribute and follows
/// the language as a DynamicResource; without one the attribute's text, the English source, is bound as a constant.
/// A binding either way, because a markup extension that returns an object for an object-typed property (a tooltip,
/// a check box's content) would have the binding object itself stored there instead of applied.
/// </summary>
public abstract class ModelMemberExtension
{
	/// <summary>Creates the extension for one member.</summary>
	/// <param name="member">Type and property, for example <c>model:Scene.Width</c>.</param>
	protected ModelMemberExtension(string member)
	{
		Member = member;
	}

	/// <summary>Type and property, for example <c>model:Scene.Width</c>.</summary>
	public string Member { get; set; }

	/// <summary>The resource key prefix of this kind of text ("Label" or "Hint").</summary>
	protected abstract string Prefix { get; }

	/// <summary>Produces the fallback text for the resolved property; null for none.</summary>
	protected abstract string? Provide(PropertyInfo property);

	/// <summary>The binding for the target property: the string table's entry when it exists, else the attribute's text.</summary>
	public BindingBase ProvideValue(IServiceProvider serviceProvider)
	{
		int dot = Member.LastIndexOf('.');
		if (dot <= 0 || dot == Member.Length - 1)
		{
			throw new ArgumentException($"'{Member}' is not of the form prefix:Type.Property.");
		}
		Type type = ResolveType(serviceProvider, Member[..dot]);
		string name = Member[(dot + 1)..];
		PropertyInfo property = type.GetProperty(name)
			?? throw new ArgumentException($"'{type.Name}' has no property '{name}'.");
		// a string table entry wins over the attribute and follows the language (a DynamicResource); the attribute is
		// the English source and the fallback (upstream, 2026-10-14; the tables are generated from the attributes)
		string key = $"{Prefix}.{type.Name}.{name}";
		if (Application.Current?.TryGetResource(key, null, out _) == true)
		{
			return new DynamicResourceExtension(key).ProvideValue(serviceProvider);
		}
		return Constant(Provide(property));
	}

	/// <summary>A one-time binding to a fixed text (or null), so the caller's target is bound the same way in both cases.</summary>
	private static BindingBase Constant(string? text) =>
		CompiledBinding.Create((ConstantSource source) => source.Value, new ConstantSource(text), mode: BindingMode.OneTime);

	/// <summary>
	/// The type a <c>prefix:Name</c> names in the document: through the XAML type resolver the compiled XAML offers;
	/// failing that, through the document's namespace table, which maps the prefix to CLR namespaces and assemblies.
	/// </summary>
	internal static Type ResolveType(IServiceProvider serviceProvider, string qualifiedName)
	{
		if (serviceProvider.GetService(typeof(IXamlTypeResolver)) is IXamlTypeResolver resolver)
		{
			try
			{
				if (resolver.Resolve(qualifiedName) is { } resolved)
				{
					return resolved;
				}
			}
			catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException or XamlLoadException)
			{
				// the resolver's failure type is not documented; the namespace table below is the second attempt
			}
		}
		int colon = qualifiedName.IndexOf(':');
		string prefix = colon < 0 ? "" : qualifiedName[..colon];
		string typeName = colon < 0 ? qualifiedName : qualifiedName[(colon + 1)..];
		if (serviceProvider.GetService(typeof(IAvaloniaXamlIlXmlNamespaceInfoProvider)) is IAvaloniaXamlIlXmlNamespaceInfoProvider namespaces
			&& namespaces.XmlNamespaces.TryGetValue(prefix, out IReadOnlyList<AvaloniaXamlIlXmlNamespaceInfo>? infos))
		{
			foreach (AvaloniaXamlIlXmlNamespaceInfo info in infos)
			{
				string full = string.IsNullOrEmpty(info.ClrNamespace) ? typeName : info.ClrNamespace + "." + typeName;
				Type? type = Type.GetType(string.IsNullOrEmpty(info.ClrAssemblyName) ? full : full + ", " + info.ClrAssemblyName, throwOnError: false)
					?? AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(full, throwOnError: false)).FirstOrDefault(t => t != null);
				if (type != null)
				{
					return type;
				}
			}
		}
		throw new ArgumentException($"The type '{qualifiedName}' could not be resolved in this document.");
	}

	/// <summary>A holder for the constant text the fallback binds to.</summary>
	/// <param name="value">The text, or null.</param>
	public sealed class ConstantSource(string? value)
	{
		/// <summary>The text.</summary>
		public string? Value { get; } = value;
	}
}

/// <summary>The property's label: the string table's <c>Label.Type.Prop</c>, else its <see cref="DisplayNameAttribute"/>, else its name.</summary>
public sealed class LabelExtension : ModelMemberExtension
{
	/// <inheritdoc/>
	public LabelExtension(string member) : base(member)
	{
	}

	/// <inheritdoc/>
	protected override string Prefix => "Label";

	/// <inheritdoc/>
	protected override string Provide(PropertyInfo property) =>
		property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? property.Name;
}

/// <summary>The property's tooltip: the string table's <c>Hint.Type.Prop</c>, else its <see cref="DescriptionAttribute"/> text, else null (no tooltip).</summary>
public sealed class HintExtension : ModelMemberExtension
{
	/// <inheritdoc/>
	public HintExtension(string member) : base(member)
	{
	}

	/// <inheritdoc/>
	protected override string Prefix => "Hint";

	/// <inheritdoc/>
	protected override string? Provide(PropertyInfo property) =>
		property.GetCustomAttribute<DescriptionAttribute>()?.Description;
}
