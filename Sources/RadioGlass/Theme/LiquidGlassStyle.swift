import SwiftUI

extension View {
    func glassPanel() -> some View {
        self
            .background(
                RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .fill(Color.white.opacity(0.035))
                    .overlay(
                        RoundedRectangle(cornerRadius: 10, style: .continuous)
                            .strokeBorder(Color.white.opacity(0.07), lineWidth: 1)
                    )
            )
    }

    func chromePanel() -> some View {
        self
            .background(
                RoundedRectangle(cornerRadius: 0, style: .continuous)
                    .fill(Color.black.opacity(0.24))
            )
    }

    func playerPanel() -> some View {
        self
            .background(
                RoundedRectangle(cornerRadius: 16, style: .continuous)
                    .fill(.ultraThinMaterial)
                    .overlay(
                        RoundedRectangle(cornerRadius: 16, style: .continuous)
                            .strokeBorder(Color.white.opacity(0.10), lineWidth: 1)
                    )
                    .shadow(color: Color.black.opacity(0.35), radius: 18, x: 0, y: 8)
            )
    }

    func appBackground() -> some View {
        self
            .background(
                LinearGradient(
                    colors: [
                        Color(red: 0.09, green: 0.09, blue: 0.10),
                        Color(red: 0.11, green: 0.11, blue: 0.12)
                    ],
                    startPoint: .top,
                    endPoint: .bottom
                )
            )
            .ignoresSafeArea()
    }
}
