# Testathon Audience 多屏输出技术报告

## 目标

在 `Testathon_audience` 场景中，将两个基于同一观众眼睛位置计算的离轴视锥分别输出到投影仪和第二块实体屏幕，并保留后续扩展到更多屏幕的能力。

## 实现内容

- 新增场景专用的 `MultiDisplayOutputManager`，在应用启动时检测并记录全部 Unity Display。
- 在 Windows Standalone Build 中自动调用 `Display.Activate()` 激活额外显示器。
- 自动发现并按名称排序 `PortalCameraController`，随后按顺序分配输出屏幕。
- 默认保留 API 索引 0（Inspector 的 Display 1）作为控制屏，将两台 Portal Camera 分配到 API 索引 1、2（Inspector 的 Display 2、3）。
- 如果现场只有两块显示设备，自动回退到 API 索引 0、1（Inspector 的 Display 1、2）。
- 如果现场只有一块显示设备，第一台 Portal Camera 回退到 API 索引 0（Inspector 的 Display 1），仅禁用第二台 Portal Camera。
- 当实际显示器不足时，禁用没有输出目标的 Portal Camera，并输出明确错误日志，不影响仍有显示目标的相机。
- 启用 `PhysicalScreen RIGHT` 与 `PortalCamera RIGHT`，使第二个窗口参与视锥计算和画面输出。
- 当右侧物理屏幕在场景中被替换后，将 `PortalCamera RIGHT` 的 `screen` 引用重新绑定到新的 `PhysicalScreen RIGHT`。

## 场景默认配置

| 对象 | 默认输出 |
|---|---|
| `PortalCamera` | API 索引 1 / Inspector Display 2（投影仪） |
| `PortalCamera RIGHT` | API 索引 2 / Inspector Display 3（第二实体屏） |

两台相机继续共享同一个 `SimulatedEye`，但分别根据自己的 `PhysicalScreen` 计算投影矩阵。

## 部署要求

1. 两个实体输出必须连接到两个独立显卡接口；普通 HDMI 分配器只能镜像，不能用于独立视图。
2. Windows 中将多显示器模式设置为“扩展这些显示器”。
3. 启动应用前连接全部屏幕。
4. 使用 Windows Standalone Build 验证真实多屏输出；Unity Editor 主要用于分别预览各 Display。
5. 如果没有单独的电脑控制屏，脚本会在检测到仅两块显示器时自动使用 Inspector Display 1 和 Display 2。
6. Unity Inspector 使用 Display 1～8 的名称；脚本 API 使用从 0 开始的索引，两者相差 1。

## 验证结果

- `Assembly-CSharp.csproj` 编译成功：0 个错误。
- 场景中的第二物理窗口和第二 Portal Camera 已启用。
- 场景默认 `targetDisplay` API 索引为 1 和 2，对应 Inspector 的 Display 2 和 Display 3。

现场仍需完成的硬件验证包括：确认 Windows/Unity 显示器索引、检查投影仪与实体屏幕是否对应正确，以及校准第二块屏幕的真实尺寸和空间位置。
