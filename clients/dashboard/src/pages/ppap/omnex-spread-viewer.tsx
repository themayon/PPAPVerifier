import { useEffect, useMemo, useRef } from "react";
import { useQuery } from "@tanstack/react-query";
import { OmnexSpread, type ISpreadsheetViewer, type SpreadsheetHighlightRange } from "omnex-spreadjs";
import { getFileDownloadUrl } from "@/api/files";
import { getSpreadsheetViewerLicense } from "@/api/ppap";
import { Skeleton } from "@/components/ui/skeleton";

export function OmnexSpreadsheetViewer({ fileAssetId, highlights = [], selected }: { fileAssetId: string; highlights?: SpreadsheetHighlightRange[]; selected?: { sheetName: string; range: string } | null }) {
  const containerRef = useRef<HTMLDivElement>(null);
  const viewerRef = useRef<ISpreadsheetViewer | null>(null);
  const highlightsRef = useRef(highlights);
  const selectedRef = useRef(selected);
  highlightsRef.current = highlights;
  selectedRef.current = selected;
  const highlightsKey = useMemo(() => JSON.stringify(highlights), [highlights]);
  const licenseQuery = useQuery({ queryKey: ["ppap", "viewer-license"], queryFn: getSpreadsheetViewerLicense, staleTime: 15 * 60 * 1000 });
  const urlQuery = useQuery({ queryKey: ["files", fileAssetId, "workbook-url"], queryFn: () => getFileDownloadUrl(fileAssetId), enabled: Boolean(fileAssetId), staleTime: 45 * 60 * 1000 });

  useEffect(() => {
    if (!containerRef.current || !licenseQuery.data || !urlQuery.data) return;
    let cancelled = false;
    let viewer: ISpreadsheetViewer | null = null;
    const initialize = async () => {
      const licensed = await OmnexSpread.setLicense(licenseQuery.data.licenseKey);
      if (!licensed) throw new Error("The OmnexSpread license is invalid for this domain.");
      viewer = await OmnexSpread.mount(containerRef.current!, { readOnly: true, showSheetTabs: true });
      const response = await fetch(urlQuery.data.url);
      if (!response.ok) throw new Error("The generated workbook could not be downloaded.");
      await viewer.loadXlsx(await response.arrayBuffer());
      if (cancelled) { viewer.destroy(); return; }
      viewerRef.current = viewer;
      viewer.highlightRanges(highlightsRef.current);
      if (selectedRef.current) viewer.selectRange(selectedRef.current.sheetName, selectedRef.current.range);
    };
    void initialize().catch((error) => {
      if (!cancelled && containerRef.current) containerRef.current.textContent = error instanceof Error ? error.message : "Unable to load the spreadsheet viewer.";
    });
    return () => {
      cancelled = true;
      viewerRef.current = null;
      viewer?.destroy();
    };
  }, [fileAssetId, licenseQuery.data, urlQuery.data]);

  useEffect(() => {
    if (!viewerRef.current) return;
    viewerRef.current.highlightRanges(JSON.parse(highlightsKey) as SpreadsheetHighlightRange[]);
    if (selectedRef.current) viewerRef.current.selectRange(selectedRef.current.sheetName, selectedRef.current.range);
  }, [highlightsKey, selected?.range, selected?.sheetName]);

  if (licenseQuery.isLoading || urlQuery.isLoading) return <Skeleton className="h-[32rem] w-full rounded-xl" />;
  if (licenseQuery.isError || urlQuery.isError) return <div role="alert" className="grid h-72 place-items-center rounded-xl border border-[var(--color-border)] bg-[var(--color-muted)] text-sm text-[var(--color-destructive)]">Unable to initialize the extracted spreadsheet viewer.</div>;
  return <div ref={containerRef} className="h-[calc(100vh-18rem)] min-h-[28rem] w-full overflow-hidden rounded-xl" />;
}
