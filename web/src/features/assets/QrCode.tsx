import { useEffect, useState } from 'react';
import QRCode from 'qrcode';

/** Renders the tag payload as an SVG QR code (error correction M, suitable for outdoor labels). */
export function QrCode({ payload, size = 200, testId = 'qr' }: { payload: string; size?: number; testId?: string }) {
  const [svg, setSvg] = useState<string>('');
  useEffect(() => {
    let cancelled = false;
    void QRCode.toString(payload, { type: 'svg', errorCorrectionLevel: 'M', margin: 2 }).then((s) => {
      if (!cancelled) setSvg(s);
    });
    return () => {
      cancelled = true;
    };
  }, [payload]);
  return <div className="qr" style={{ width: size, height: size }} data-testid={testId} data-payload={payload} dangerouslySetInnerHTML={{ __html: svg }} />;
}
