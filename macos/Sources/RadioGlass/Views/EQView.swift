import SwiftUI

struct EQView: View {
    @ObservedObject var eq: AudioEQProcessor

    var body: some View {
        VStack(spacing: 0) {
            header
            Divider()
            bandGrid
            Divider()
            presetBar
        }
        .frame(width: 380)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
        .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
    }

    // MARK: - Header

    private var header: some View {
        HStack(spacing: 10) {
            Image(systemName: "waveform.path.ecg")
                .font(.body.weight(.semibold))
                .foregroundStyle(eq.isEnabled ? Color.accentColor : .secondary)

            Text("Equalizer")
                .font(.callout.weight(.semibold))

            Spacer()

            Toggle("", isOn: $eq.isEnabled)
                .toggleStyle(.switch)
                .labelsHidden()
                .controlSize(.mini)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
    }

    // MARK: - Band Grid

    private var bandGrid: some View {
        HStack(alignment: .top, spacing: 4) {
            ForEach($eq.bands) { $band in
                BandColumn(band: $band, isEnabled: eq.isEnabled) { newGain in
                    eq.setGain(newGain, band: band.id)
                }
            }
        }
        .padding(.horizontal, 12)
        .padding(.top, 12)
        .padding(.bottom, 8)
        .opacity(eq.isEnabled ? 1 : 0.45)
        .animation(.easeInOut(duration: 0.15), value: eq.isEnabled)
    }

    // MARK: - Preset Bar

    private var presetBar: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 6) {
                ForEach(EQPreset.allCases) { preset in
                    let isSelected = eq.selectedPreset == preset

                    Button {
                        withAnimation(.spring(duration: 0.2)) {
                            eq.applyPreset(preset)
                        }
                    } label: {
                        Text(preset.rawValue)
                            .font(.caption.weight(isSelected ? .semibold : .regular))
                            .foregroundStyle(isSelected ? Color.accentColor : .secondary)
                            .padding(.horizontal, 10)
                            .padding(.vertical, 5)
                            .background(
                                isSelected
                                    ? Color.accentColor.opacity(0.12)
                                    : Color.clear,
                                in: Capsule()
                            )
                            .overlay(
                                Capsule().strokeBorder(
                                    isSelected ? Color.accentColor.opacity(0.4) : Color.secondary.opacity(0.2),
                                    lineWidth: 1
                                )
                            )
                    }
                    .buttonStyle(.plain)
                    .disabled(!eq.isEnabled)
                }
            }
            .padding(.horizontal, 12)
        }
        .padding(.vertical, 10)
        .opacity(eq.isEnabled ? 1 : 0.4)
        .animation(.easeInOut(duration: 0.15), value: eq.isEnabled)
    }
}

// MARK: - Band Column

private struct BandColumn: View {
    @Binding var band: EQBand
    let isEnabled: Bool
    let onGainChanged: (Float) -> Void

    private let sliderHeight: CGFloat = 80
    private let tickDB: [Float] = [12, 6, 0, -6, -12]

    var body: some View {
        VStack(spacing: 4) {
            // dB value
            Text(gainLabel)
                .font(.system(size: 9, weight: .medium).monospacedDigit())
                .foregroundStyle(gainColor)
                .frame(height: 12)

            // Vertical slider using drag gesture
            ZStack {
                // Track
                RoundedRectangle(cornerRadius: 2)
                    .fill(Color.secondary.opacity(0.15))
                    .frame(width: 4, height: sliderHeight)

                // Fill
                Rectangle()
                    .fill(gainColor.opacity(0.6))
                    .frame(width: 4, height: fillHeight)
                    .offset(y: fillOffset)

                // Thumb
                Circle()
                    .fill(isEnabled ? Color.accentColor : Color.secondary)
                    .frame(width: 12, height: 12)
                    .offset(y: thumbOffset)
                    .shadow(color: .black.opacity(0.25), radius: 2, x: 0, y: 1)
            }
            .frame(width: 32, height: sliderHeight + 12)
            .contentShape(Rectangle())
            .gesture(
                DragGesture(minimumDistance: 0)
                    .onChanged { value in
                        guard isEnabled else { return }
                        let pct = Double((-value.location.y + sliderHeight / 2)) / Double(sliderHeight)
                        let newGain = Float(pct * 24 - 12)
                        onGainChanged(max(-12, min(12, newGain)))
                    }
            )

            // Frequency label
            Text(band.label)
                .font(.system(size: 9, weight: .medium))
                .foregroundStyle(.secondary)
                .frame(height: 12)
        }
        .frame(maxWidth: .infinity)
    }

    private var gainLabel: String {
        let g = band.gain
        if abs(g) < 0.1 { return "0" }
        return g > 0 ? "+\(String(format: "%.1f", g))" : String(format: "%.1f", g)
    }

    private var gainColor: Color {
        let g = band.gain
        if abs(g) < 0.5 { return .secondary }
        return g > 0 ? Color.accentColor : Color.orange
    }

    private var thumbOffset: CGFloat {
        let pct = CGFloat(band.gain + 12) / 24
        return (0.5 - pct) * sliderHeight
    }

    private var fillHeight: CGFloat {
        abs(CGFloat(band.gain) / 12 * sliderHeight / 2)
    }

    private var fillOffset: CGFloat {
        let half = sliderHeight / 2
        let g = CGFloat(band.gain)
        if g >= 0 {
            return -(g / 12 * half / 2)
        } else {
            return (-g / 12 * half / 2)
        }
    }
}

// MARK: - Compact EQ indicator for toolbar use

struct EQStatusBadge: View {
    @ObservedObject var eq: AudioEQProcessor

    var body: some View {
        HStack(spacing: 3) {
            Image(systemName: "waveform.path.ecg")
                .font(.caption2.weight(.semibold))
            if eq.isEnabled, let preset = eq.selectedPreset {
                Text(preset.rawValue)
                    .font(.caption2)
                    .lineLimit(1)
            }
        }
        .foregroundStyle(eq.isEnabled ? Color.accentColor : .secondary)
        .padding(.horizontal, 6)
        .padding(.vertical, 3)
        .background(
            eq.isEnabled
                ? Color.accentColor.opacity(0.12)
                : Color.secondary.opacity(0.08),
            in: Capsule()
        )
    }
}
