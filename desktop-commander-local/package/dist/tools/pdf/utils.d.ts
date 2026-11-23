/**
 * Normalize page indexes, handling negative indices and removing duplicates
 */
export declare const normalizePageIndexes: (pageIndexes: number[], pageCount: number) => number[];
/**
 * Generate page numbers based on offset and length
 * @param offset Zero-based offset or negative for the last pages (length is then ignored, as for lines)
 * @param length Number of pages to generate
 * @param totalPages Total number of pages in the document
 * @returns Array of page numbers (empty when the offset is past the last page or length is 0)
 */
export declare function generatePageNumbers(offset: number, length: number, totalPages: number): number[];
/**
 * Close a PDF document opened with unpdf, so pdf.js frees what it holds for it
 * @param pdfDocument The document, closed through its loading task: unpdf 1.8's
 * document has no destroy() of its own (1.4's only calls loadingTask.destroy())
 */
export declare function closePdfDocument(pdfDocument: {
    loadingTask: {
        destroy(): Promise<void>;
    };
}): Promise<void>;
