class UpdatedItem {
  final String name;
  final String receiptName;
  final double oldPrice;
  final double newPrice;

  UpdatedItem({
    required this.name,
    required this.receiptName,
    required this.oldPrice,
    required this.newPrice,
  });

  factory UpdatedItem.fromJson(Map<String, dynamic> json) => UpdatedItem(
        name: json['name'] as String,
        receiptName: json['receipt_name'] as String,
        oldPrice: (json['old_price'] as num).toDouble(),
        newPrice: (json['new_price'] as num).toDouble(),
      );
}

class InsertedItem {
  final String name;
  final double price;

  InsertedItem({required this.name, required this.price});

  factory InsertedItem.fromJson(Map<String, dynamic> json) => InsertedItem(
        name: json['name'] as String,
        price: (json['price'] as num).toDouble(),
      );
}

class UnchangedItem {
  final String name;
  final String receiptName;
  final double currentPrice;
  final double receiptPrice;

  UnchangedItem({
    required this.name,
    required this.receiptName,
    required this.currentPrice,
    required this.receiptPrice,
  });

  factory UnchangedItem.fromJson(Map<String, dynamic> json) => UnchangedItem(
        name: json['name'] as String,
        receiptName: json['receipt_name'] as String,
        currentPrice: (json['current_price'] as num).toDouble(),
        receiptPrice: (json['receipt_price'] as num).toDouble(),
      );
}

/// Rispecchia il JSON restituito da `POST /process/` in App_Python/main.py.
class ProcessResult {
  final int extractedCount;
  final int updatedCount;
  final int insertedCount;
  final int unchangedCount;
  final List<UpdatedItem> updated;
  final List<InsertedItem> inserted;
  final List<UnchangedItem> unchanged;
  final String downloadUrl;

  ProcessResult({
    required this.extractedCount,
    required this.updatedCount,
    required this.insertedCount,
    required this.unchangedCount,
    required this.updated,
    required this.inserted,
    required this.unchanged,
    required this.downloadUrl,
  });

  factory ProcessResult.fromJson(Map<String, dynamic> json) => ProcessResult(
        extractedCount: json['extracted_count'] as int,
        updatedCount: json['updated_count'] as int,
        insertedCount: json['inserted_count'] as int,
        unchangedCount: json['unchanged_count'] as int,
        updated: (json['updated'] as List)
            .map((e) => UpdatedItem.fromJson(e as Map<String, dynamic>))
            .toList(),
        inserted: (json['inserted'] as List)
            .map((e) => InsertedItem.fromJson(e as Map<String, dynamic>))
            .toList(),
        unchanged: (json['unchanged'] as List)
            .map((e) => UnchangedItem.fromJson(e as Map<String, dynamic>))
            .toList(),
        downloadUrl: json['download_url'] as String,
      );
}
