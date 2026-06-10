export function normalizeProjectDataType(dataType) {
  return String(dataType || "").toLowerCase() === "segmentation" ? "segmentation" : "detection";
}

export function projectDataTypeLabel(dataType) {
  return normalizeProjectDataType(dataType) === "segmentation" ? "实例分割" : "目标检测";
}

export function projectYoloExportHint(dataType) {
  return normalizeProjectDataType(dataType) === "segmentation"
    ? "YOLO segment（多边形坐标）"
    : "YOLO detect（矩形框 cx cy w h）";
}

export function buildYoloLine(item, dataType) {
  if (normalizeProjectDataType(dataType) === "segmentation" && item.segment?.length >= 6) {
    return `${item.classId} ${item.segment.map((value) => Number(value).toFixed(6)).join(" ")}`;
  }

  const box = item.bbox || { cx: 0.5, cy: 0.5, width: 0.2, height: 0.2 };
  return `${item.classId} ${Number(box.cx).toFixed(6)} ${Number(box.cy).toFixed(6)} ${Number(box.width).toFixed(6)} ${Number(box.height).toFixed(6)}`;
}

export function buildYoloTxt(annotations, dataType, isExportable) {
  return (annotations || [])
    .filter(isExportable)
    .map((item) => buildYoloLine(item, dataType))
    .join("\n");
}
