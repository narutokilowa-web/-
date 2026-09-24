import 'package:flutter_test/flutter_test.dart';
import 'package:mfop_field/core/pin.dart';
import 'package:mfop_field/core/secure_store.dart';
import 'package:mfop_field/core/tag_token.dart';

void main() {
  test('PIN is stored as a salted hash and verified; format is 4–8 digits', () async {
    final store = MemorySecureStore();
    final pin = PinService(store, iterations: 10);
    expect(() => pin.set('12a4'), throwsArgumentError);
    await pin.set('2468');
    expect(store.values[SecureKeys.pinHash], isNot(contains('2468')));
    expect(await pin.verify('2468'), isTrue);
    expect(await pin.verify('1111'), isFalse);
    expect(pin.failures, 1);
    for (var i = 0; i < 4; i++) {
      await pin.verify('0000');
    }

    expect(pin.exhausted, isTrue);
  });

  test('tag tokens from QR URLs and typed codes', () {
    expect(tagTokenFrom('http://localhost:5173/t/7K3M92QX.4F'), '7K3M92QX.4F');
    expect(tagTokenFrom('https://municipality.example/t/7K3M92QX.4F'), '7K3M92QX.4F');
    expect(tagTokenFrom(' 7k3m 92qx-4f '), '7K3M92QX.4F');
    expect(tagTokenFrom('7K3M92QX4F'), '7K3M92QX.4F');
    expect(tagTokenFrom('SL-000001'), isNull);
    expect(tagTokenFrom(''), isNull);
  });
}
