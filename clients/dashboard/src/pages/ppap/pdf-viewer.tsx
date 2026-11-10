import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ChevronLeft, ChevronRight, Download, Maximize2, ZoomIn, ZoomOut } from "lucide-react";
import { Document, Page, pdfjs } from "react-pdf";
import "react-pdf/dist/Page/AnnotationLayer.css";
import "react-pdf/dist/Page/TextLayer.css";
import { getFileDownloadUrl } from "@/api/files";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";

pdfjs.GlobalWorkerOptions.workerSrc = new URL("pdfjs-dist/build/pdf.worker.min.mjs", import.meta.url).toString();

export type PdfEvidenceBox = {
  pageNumber: number;
  x: number;
  y: number;
  width: number;
  height: number;
  label?: string;
};

export function PpapPdfViewer({ fileAssetId, evidence = [], activePage }: { fileAssetId: string; evidence?: PdfEvidenceBox[]; activePage?: number }) {
  const [pageNumber, setPageNumber] = useState(activePage ?? 1);
  const [pageCount, setPageCount] = useState(0);
  const [scale, setScale] = useState(1);
  const query = useQuery({
    queryKey: ["files", fileAssetId, "inline-url"],
    queryFn: () => getFileDownloadUrl(fileAssetId, { inline: true }),
    enabled: Boolean(fileAssetId),
    staleTime: 45 * 60 * 1000,
  });
  const pageEvidence = evidence.filter((item) => item.pageNumber === pageNumber);

  if (query.isLoading) return <Skeleton className="h-[32rem] w-full rounded-xl" />;
  if (query.isError || !query.data) return <div role="alert" className="grid h-72 place-items-center rounded-xl border border-[var(--color-border)] bg-[var(--color-muted)] text-sm text-[var(--color-destructive)]">Unable to load the source PDF.</div>;

  return <section className="overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-card)]">
    <div className="flex flex-wrap items-center justify-between gap-2 border-b border-[var(--color-border)] px-3 py-2">
      <div className="flex items-center gap-1">
        <Button variant="ghost" size="icon-xs" aria-label="Previous PDF page" disabled={pageNumber <= 1} onClick={() => setPageNumber((value) => Math.max(1, value - 1))}><ChevronLeft /></Button>
        <span className="min-w-20 text-center text-xs font-semibold tabular-nums">{pageNumber} / {pageCount || "—"}</span>
        <Button variant="ghost" size="icon-xs" aria-label="Next PDF page" disabled={pageNumber >= pageCount} onClick={() => setPageNumber((value) => Math.min(pageCount, value + 1))}><ChevronRight /></Button>
      </div>
      <div className="flex items-center gap-1">
        <Button variant="ghost" size="icon-xs" aria-label="Zoom out" onClick={() => setScale((value) => Math.max(0.5, value - 0.1))}><ZoomOut /></Button>
        <span className="min-w-12 text-center text-xs tabular-nums">{Math.round(scale * 100)}%</span>
        <Button variant="ghost" size="icon-xs" aria-label="Zoom in" onClick={() => setScale((value) => Math.min(2.5, value + 0.1))}><ZoomIn /></Button>
        <Button asChild variant="ghost" size="icon-xs"><a href={query.data.url} target="_blank" rel="noreferrer" aria-label="Open PDF in new window"><Maximize2 /></a></Button>
        <Button asChild variant="ghost" size="icon-xs"><a href={query.data.url} download aria-label="Download PDF"><Download /></a></Button>
      </div>
    </div>
    <div className="max-h-[calc(100vh-18rem)] overflow-auto bg-[#242933] p-4">
      <Document file={query.data.url} loading={<Skeleton className="mx-auto h-[40rem] max-w-3xl" />} onLoadSuccess={({ numPages }) => { setPageCount(numPages); setPageNumber((value) => Math.min(value, numPages)); }} onLoadError={() => setPageCount(0)}>
        <div className="relative mx-auto w-fit bg-white shadow-xl">
          <Page pageNumber={pageNumber} scale={scale} renderAnnotationLayer renderTextLayer />
          <div className="pointer-events-none absolute inset-0" aria-hidden>
            {pageEvidence.map((box, index) => <span key={`${box.pageNumber}-${index}`} title={box.label} className="absolute border-2 border-[var(--color-destructive)] bg-[oklch(from_var(--color-destructive)_l_c_h_/_0.16)]" style={{ left: `${box.x * 100}%`, top: `${box.y * 100}%`, width: `${box.width * 100}%`, height: `${box.height * 100}%` }} />)}
          </div>
        </div>
      </Document>
    </div>
  </section>;
}
