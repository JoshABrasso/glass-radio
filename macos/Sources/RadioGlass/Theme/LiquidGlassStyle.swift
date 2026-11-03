import AppKit
import SwiftUI

enum RadioGlassTheme {
    static let appBackground = Color(nsColor: .windowBackgroundColor)
    static let sidebarBackground = Color(nsColor: .underPageBackgroundColor)
    static let contentBackground = Color(nsColor: .controlBackgroundColor)
    static let panelFill = Color(nsColor: .controlBackgroundColor)
    static let separator = Color(nsColor: .separatorColor)
    static let primaryText = Color.primary
    static let secondaryText = Color.secondary
    static let tertiaryText = Color(nsColor: .tertiaryLabelColor)
    static let selectionFill = Color.accentColor.opacity(0.16)
    static let hoverFill = Color.primary.opacity(0.06)
    static let subtleFill = Color.primary.opacity(0.035)
    static let prominentControlFill = Color.accentColor
    static let prominentControlText = Color(nsColor: .selectedControlTextColor)
    static let favorite = Color(nsColor: .systemYellow)
    static let live = Color(nsColor: .systemGreen)
    static let paused = Color(nsColor: .systemOrange)
    static let destructive = Color(nsColor: .systemRed)
}

extension View {
    func glassPanel() -> some View {
        self
            .background(
                RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .fill(.regularMaterial)
                    .overlay(
                        RoundedRectangle(cornerRadius: 10, style: .continuous)
                            .strokeBorder(RadioGlassTheme.separator.opacity(0.55), lineWidth: 1)
                    )
            )
    }

    func chromePanel() -> some View {
        self
            .background(
                RoundedRectangle(cornerRadius: 0, style: .continuous)
                    .fill(RadioGlassTheme.sidebarBackground.opacity(0.82))
            )
    }

    func playerPanel() -> some View {
        self
            .background(
                RoundedRectangle(cornerRadius: 16, style: .continuous)
                    .fill(.regularMaterial)
                    .overlay(
                        RoundedRectangle(cornerRadius: 16, style: .continuous)
                            .strokeBorder(RadioGlassTheme.separator.opacity(0.7), lineWidth: 1)
                    )
                    .shadow(color: Color.black.opacity(0.18), radius: 18, x: 0, y: 8)
            )
    }

    func appBackground() -> some View {
        self
            .background(RadioGlassTheme.appBackground)
            .ignoresSafeArea()
    }
}
