import 'package:flutter_test/flutter_test.dart';

import 'package:app_androflutter/main.dart';

void main() {
  testWidgets('Home page shows the upload form', (WidgetTester tester) async {
    await tester.pumpWidget(const ShoppingListApp());

    expect(find.text('Aggiorna Spesa da Scontrino'), findsWidgets);
    expect(find.text('1. Carica il file ZIP di Backup (.zip)'), findsOneWidget);
    expect(find.text('2. Carica la foto dello Scontrino'), findsOneWidget);
    expect(find.text('Elabora Scontrino'), findsOneWidget);
  });
}
