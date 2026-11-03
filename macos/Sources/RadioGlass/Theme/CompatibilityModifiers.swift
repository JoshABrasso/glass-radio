import SwiftUI

// MARK: - Backward-compatible onChange

extension View {
    /// Observes changes to `value` and calls `action` on both macOS 13 and 14+.
    /// On macOS 14+ the modern 2-param body form is used. On macOS 13 the
    /// deprecated-but-functional perform: form is used as the fallback.
    func onChangeCompat<V: Equatable>(of value: V, perform action: @escaping () -> Void) -> some View {
        modifier(CompatChangeObserver(value: value, action: action))
    }
}

private struct CompatChangeObserver<V: Equatable>: ViewModifier {
    let value: V
    let action: () -> Void

    func body(content: Content) -> some View {
        if #available(macOS 14.0, *) {
            content.onChange(of: value) { _, _ in action() }
        } else {
            content.onChange(of: value) { _ in action() }
        }
    }
}
